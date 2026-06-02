<div align="center">

# ⚡ TickForge

**A multi-exchange, low-latency L2 order book engine with built-in latency instrumentation.**

Built on .NET 10 / C# 14.

[![CI](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml/badge.svg)](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)

</div>

TickForge ingests live market data from multiple crypto exchanges, maintains a
consistent Level-2 order book per venue under an unreliable wire (snapshot
bootstrap, sequence-gap detection, resync), and measures its own processing
latency honestly. It ships with a console host, a live web dashboard, a
benchmark suite, a test project, and CI.

> The goal isn't to trade. It's to build the parts that are actually hard:
> protocol parsing, book consistency under packet loss, an allocation-free hot
> path, and a latency number you can defend.

![TickForge dashboard](docs/dashboard.png)

---

## Highlights

- **Exchange-agnostic core.** Adding a venue never touches `OrderBook`. Every
  adapter normalizes its wire format to a single `LevelChange (Side, Price,
  Quantity)` shape, where `Quantity == 0` means "remove this level".
- **Two real consistency models.** Binance (REST snapshot + diff stream with
  `U`/`u` reconciliation) and Coinbase (Advanced Trade `level2` with a global
  `sequence_num`) each own their bootstrap and gap → resync logic, isolated
  behind a pure, unit-testable reconciler.
- **Allocation-free hot path.** Frames are read into pooled buffers and handed to
  the book as `ReadOnlySpan<LevelChange>` — no per-message heap traffic. A
  hand-rolled `Utf8JsonReader` parser allocates **0 bytes/frame** versus ~5.4 KB
  for the equivalent source-generated path.
- **Honest latency.** Internal tick-to-process latency is recorded into an
  HdrHistogram with coordinated-omission correction and surfaced as
  p50/p90/p99/p99.9. Exchange-to-local latency is deliberately *not* reported —
  the clocks aren't synchronized, so that number would be clock skew, not signal.
- **Production-minded.** Central package management, `TreatWarningsAsErrors`,
  29 unit tests, a BenchmarkDotNet project, a multi-stage Dockerfile, and a
  GitHub Actions pipeline.

---

## How it works

### Module map

```
TickForge.Core        Domain + abstractions. No I/O, no dependencies.
  Abstractions/         Side, InstrumentId, LevelChange, IBookSink, IFeedAdapter
  Book/                 OrderBook, BookSide
  Time/                 Clock (monotonic nanoseconds)

TickForge.Latency     LatencyRecorder over HdrHistogram + LatencyBookSink. → Core

TickForge.Feeds       One adapter per exchange. → Core
  Common/               WebSocketFeedBase (connect, reconnect, frame read)
  Binance/              REST snapshot + diff-stream reconciliation
  Coinbase/             level2 + sequence_num reconciliation

TickForge.App         Console host: adapters → sink → book → latency.
TickForge.Web         ASP.NET host + WebSocket + dashboard.
TickForge.Benchmarks  BenchmarkDotNet micro-benchmarks.
TickForge.Tests       xUnit: book, gap/resync, latency, parser parity.
```

Dependencies point strictly inward: everything references `Core`; `Core`
references nothing.

### Data flow

```
exchange ──▶ feed adapter ──▶ IBookSink ──▶ OrderBook
                  │                              │
            (parse + recv ts)            LatencyRecorder ──▶ console / dashboard
```

The adapter is the only component that understands a specific wire format. It
parses raw frames into a normalized `LevelChange` batch, captures a receive
timestamp the instant the frame leaves the socket, and calls
`OnSnapshot` / `OnDelta` / `OnResync` on the sink. The sink applies the span to
the book and records the processing latency.

### Book consistency

Both exchanges publish the **absolute** resting quantity at a price level, but
they bootstrap and recover differently — and both live entirely inside their
adapters.

- **Binance** opens the `@depth` diff stream and buffers events, fetches the REST
  snapshot, drops stale buffered events (`u <= lastUpdateId`), and applies from
  the first event satisfying `U <= lastUpdateId + 1 <= u`. In steady state each
  event's `U` must continue from the previous `u + 1`; a hole triggers a resync
  and re-bootstrap.
- **Coinbase** subscribes to `level2`, takes the first `snapshot` as the book,
  then applies `update`s. The connection's `sequence_num` is **global** (it also
  counts the subscriptions ack), so continuity is checked against every message
  while only `l2_data` mutates the book. A break in the sequence triggers a
  resync and re-subscribe.

### Latency measurement

TickForge measures **internal tick-to-process latency**: the interval from when a
frame is read off the socket (`Clock.NowNanos()`, monotonic) to when
`OrderBook.Apply` returns — a single-clock, defensible metric.

`LatencyRecorder` wraps an HdrHistogram and records via
`RecordValueWithExpectedInterval` to correct for **coordinated omission**: the
bias that appears when a stalled consumer fails to record the long-tail samples
the stall itself caused. That's why the p99.9 tail stays honest under load.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full design rationale.

---

## Performance

Reproduce locally with `dotnet run -c Release --project bench/TickForge.Benchmarks`.

| Benchmark | What it compares |
| --- | --- |
| Depth parsing | Hand-rolled `Utf8JsonReader` vs source-generated DTO parsing |
| `OrderBook.ApplyDelta` | Applying a delta batch to a deep book |
| `BookSide` decimal vs `long` ticks | The `decimal` correctness vs scaled-integer speed trade-off |

The hand-rolled parser writes straight into a reused buffer and allocates **zero
bytes per frame**, against **~5.4 KB** for the source-generated path that
materializes `string[][]` plus a `string` per price/quantity — the dominant cost
on the per-message path.

---

## Getting started

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/download). The
public market-data feeds used here need no API keys.

```bash
git clone https://github.com/orhanyarkin/TickForge.git
cd TickForge

dotnet build -c Release
dotnet test
```

### Console host

```bash
# Both exchanges, BTC/USDT, live top-of-book + p50/p99 once a second:
dotnet run -c Release --project src/TickForge.App -- --exchange all --symbol BTC/USDT

# A single exchange:
dotnet run -c Release --project src/TickForge.App -- --exchange binance --symbol BTC/USDT
```

### Web dashboard

```bash
dotnet run -c Release --project src/TickForge.Web
# open http://localhost:5055
```

A dark, trading-terminal UI streams a fresh snapshot over WebSocket ~10×/second:
per-exchange order-book ladders with depth bars, best bid/ask/spread/mid, and live
latency chips. `GET /api/state` returns the same payload as JSON. Symbol and
exchanges are configurable via `TICKFORGE_SYMBOL` and `TICKFORGE_EXCHANGES`.

### Docker

```bash
docker compose -f infra/docker-compose.yml up --build
# open http://localhost:5055
```

The image is multi-stage (SDK build → `aspnet:10.0` runtime) and runs as a
non-root user.

---

## Project layout

```
src/
  TickForge.Core/         domain model, order book, monotonic clock
  TickForge.Latency/      HdrHistogram recorder + latency-recording sink
  TickForge.Feeds/        Binance + Coinbase adapters, WebSocket transport
  TickForge.App/          console host
  TickForge.Web/          ASP.NET + WebSocket dashboard
bench/
  TickForge.Benchmarks/   BenchmarkDotNet suite
tests/
  TickForge.Tests/        xUnit
infra/
  Dockerfile, docker-compose.yml
docs/
  ARCHITECTURE.md
```

---

## Testing

```bash
dotnet test
```

The suite covers the parts most likely to break:

- **Order book** — snapshot replace, delta apply, zero-quantity removal, best
  bid/ask selection, spread/mid math, empty-book edges, version monotonicity.
- **Sequencing** — Binance gap detection and bootstrap-boundary selection;
  Coinbase non-contiguous `sequence_num` (including the "other message advances
  the counter without a false gap" case found during live testing).
- **Latency** — percentile accuracy on a known distribution and that
  coordinated-omission correction actually inflates the tail under a stall.
- **Parser parity** — the hand-rolled parser produces identical output to the
  source-generated path.

The feed adapters are tested without a live socket: the reconcilers are pure and
the snapshot source is an interface, so sequence scenarios are driven
deterministically from in-memory data.

---

## Design decisions & trade-offs

- **`decimal` for price/quantity.** Correct and readable, but slower than scaled
  `long` ticks. The benchmark project quantifies the gap; production keeps
  `decimal` and treats the integer variant as an experiment.
- **`SortedDictionary` per book side.** Chosen for clarity. A flat,
  price-indexed array is faster for dense books and is a natural next benchmark.
- **Source-gen JSON on the live path, hand-rolled parser benchmarked alongside.**
  The allocation-free reader is proven equivalent by tests and kept as the
  documented fast path.
- **What we don't measure.** Exchange-to-local latency. The exchange timestamp
  and the local clock aren't synchronized, so reporting the difference would be
  dishonest.

---

## License

Released under the [MIT License](LICENSE).
