# Architecture

TickForge is a multi-exchange, low-latency L2 order book engine with built-in
latency instrumentation. This document explains the design: how the pieces fit
together, how each exchange keeps its book consistent over an unreliable wire,
and how latency is measured honestly.

## Design goals

1. **Exchange-agnostic core.** Adding an exchange must never touch `OrderBook`.
2. **Allocation-free hot path.** No per-message heap allocations in steady state.
   Parsing writes into pooled buffers; the book receives spans.
3. **Honest latency.** Measure what can be defended (internal processing latency)
   and state plainly what is not measured (exchange-to-local, due to clock skew).
4. **Production-minded.** Central package management, CI, Docker, tests, and a
   benchmark project — not a single-file script.

## Module map

```
TickForge.Core        Domain + abstractions. No I/O, no dependencies.
  Abstractions/       Side, InstrumentId, LevelChange, IBookSink, IFeedAdapter
  Book/               OrderBook, BookSide
  Time/               Clock (monotonic nanoseconds)

TickForge.Latency     LatencyRecorder over HdrHistogram + LatencyBookSink. → Core

TickForge.Feeds       One adapter per exchange. → Core
  Common/             WebSocketFeedBase (connect, reconnect, frame read)
  Binance/            BinanceFeedAdapter + reconciler + parsers
  Coinbase/           CoinbaseFeedAdapter + reconciler + parser

TickForge.App         Console host. Wires adapters → sink → book → latency.
TickForge.Web         ASP.NET host + WebSocket + live dashboard.
TickForge.Benchmarks  BenchmarkDotNet: parsing and book-apply micro-benchmarks.
TickForge.Tests       xUnit: book correctness, sequence/resync, latency, parser.
```

Dependency direction is strictly inward: everything points at `Core`; `Core`
points at nothing.

## Data flow

```
exchange ──▶ feed adapter ──▶ IBookSink ──▶ OrderBook
                  │                              │
            (parse + recv ts)            LatencyRecorder
```

The adapter is the only component that understands a specific wire format. It
parses raw frames into a normalized `LevelChange[]`, captures a receive timestamp
the instant the frame is read off the socket, and calls
`OnSnapshot` / `OnDelta` / `OnResync` on the sink. The sink applies the span to
the book and records the processing latency.

## The normalized model

Both Binance and Coinbase publish the **absolute** resting quantity at a price
level, not a delta. A quantity of `0` means "remove this level". `LevelChange`
captures exactly that: `(Side, Price, Quantity)` with `Quantity == 0` ⇒ removal.
Every adapter normalizes to this shape before the data crosses the `IBookSink`
boundary, so the book never branches on exchange identity.

## Book consistency — two models

This is the core engineering content. The two exchanges require different
bootstrap-and-repair logic, and both live entirely inside their adapters behind a
pure, I/O-free reconciler that is unit-tested without a socket.

### Binance — diff stream + REST snapshot

Wire: `wss://stream.binance.com:9443/ws/<sym>@depth` (diff events) plus a REST
snapshot from `/api/v3/depth?symbol=<SYM>&limit=1000`. Each diff event carries
`U` (first update id) and `u` (final update id).

Bootstrap:

1. Open the WS stream and buffer diff events.
2. Fetch the REST snapshot; note its `lastUpdateId`.
3. Drop buffered events where `u <= lastUpdateId`.
4. Apply from the first event satisfying `U <= lastUpdateId + 1 <= u`.
5. Apply that event and every subsequent buffered event.

Steady state: each new event's `U` must equal the previous event's `u + 1`. If
not, a message was lost → emit `OnResync` and re-run bootstrap.

### Coinbase — level2 channel

Wire: `wss://advanced-trade-ws.coinbase.com`, subscribing to the `level2` channel
(public, no auth). The first message for a product is a `snapshot`; subsequent
messages are `update`s. Each `updates[]` entry is
`{ side, price_level, new_quantity }`, where `new_quantity` is absolute and `"0"`
removes the level.

The envelope's `sequence_num` increments per message across the **whole
connection**, not per channel — the subscriptions ack consumes a number too. So
continuity is validated against every message received while only `l2_data`
messages mutate the book. A non-contiguous `sequence_num` indicates a gap → emit
`OnResync` and re-subscribe for a fresh snapshot.

> This subtlety is easy to get wrong: filtering to only `l2_data` before checking
> the sequence makes the numbers look non-contiguous and produces a false resync
> loop. TickForge counts every message and is covered by a regression test.

## Latency measurement

TickForge measures **internal tick-to-process latency**: the interval from the
moment a frame is read off the socket (`Clock.NowNanos()`) to the moment
`OrderBook.Apply` returns. This is a clean, single-clock, defensible metric.

It explicitly does **not** report exchange-to-local latency: the exchange event
timestamp and the local clock are not synchronized, so the difference is
dominated by clock skew, not real network or processing time.

`Clock` uses `Stopwatch.GetTimestamp()` (monotonic, sub-microsecond), never
`DateTime`. `LatencyRecorder` wraps an HdrHistogram and uses
`RecordValueWithExpectedInterval` to correct for **coordinated omission** — the
bias that appears when a stalled consumer fails to record the long-tail samples
that the stall itself caused.

## Known trade-offs

- `BookSide` uses `SortedDictionary<decimal, decimal>` for clarity. A flat,
  price-indexed array is faster for dense books; the benchmark project is where
  that comparison belongs.
- `decimal` is correct for price/quantity but slower than scaled `long` ticks.
  The choice is documented and the scaled-integer variant is benchmarked, not
  shipped.
- Live parsing uses a source-generated JSON context; an allocation-free
  hand-rolled `Utf8JsonReader` parser is benchmarked alongside it and proven
  equivalent by tests.
