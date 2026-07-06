<div align="center">

**English** | [Türkçe](README.tr.md)

</div>

<div align="center">

# TickForge

**Multi-exchange, low-latency L2 order book engine — with built-in latency measurement.**

Built on .NET 10 / C# 14.

[![CI](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml/badge.svg)](https://github.com/orhanyarkin/TickForge/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)

</div>

TickForge consumes live market data from multiple crypto exchanges, maintains a consistent Level-2 order book per exchange over an unreliable connection (snapshot bootstrap, sequence-gap detection, resync), and honestly measures its own processing latency. It ships with a console host, a live web dashboard, a benchmark suite, a test project and CI.

> The goal isn't to trade. It's to build the genuinely hard parts: protocol parsing, book consistency under packet loss, an allocation-free hot path and a latency number you can defend.

---

## Highlights

- **Exchange-agnostic core.** Adding a new exchange never touches `OrderBook`. Each adapter normalizes the wire format into a single `LevelChange (Side, Price, Quantity)` shape; `Quantity == 0` removes the level.
- **Two real consistency models.** Binance (REST snapshot + diff stream, `U`/`u` reconciliation) and Coinbase (Advanced Trade `level2`, global `sequence_num`) each run their own bootstrap and gap → resync logic, isolated behind a pure, testable reconciler.
- **Allocation-free parse path.** The **live** path of both exchanges uses hand-written `Utf8JsonReader` parsers that write directly into a reused buffer without producing DTOs or intermediate `string[][]` objects — **0 bytes per frame** (the equivalent source-generated path spends ~5.4 KB). The parsed span reaches the book with zero copies. One exception: since the book is a `SortedDictionary`, a price level seen for the first time allocates a node — a documented trade-off (a flat-array variant is being benchmarked).
- **Honest latency.** Internal tick-to-process latency is recorded into a coordinated-omission-corrected HdrHistogram and reported as p50/p90/p99/p99.9. Exchange-to-local latency is **deliberately not reported** — the clocks aren't synchronized, so that number would be clock skew, not signal.
- **Production-minded.** Central package management, `TreatWarningsAsErrors`, 29 unit tests, a BenchmarkDotNet project, a multi-stage Dockerfile and GitHub Actions CI.

---

## How it works

### Module map

```
TickForge.Core        Domain + abstractions. No I/O, no dependencies.
  Abstractions/         Side, InstrumentId, LevelChange, IBookSink, IFeedAdapter
  Book/                 OrderBook, BookSide
  Time/                 Clock (monotonic nanoseconds)

TickForge.Latency     LatencyRecorder + LatencyBookSink on top of HdrHistogram. → Core

TickForge.Feeds       One adapter per exchange. → Core
  Common/               WebSocketFeedBase (connect, reconnect, frame read)
  Binance/              REST snapshot + diff-stream reconciliation
  Coinbase/             level2 + sequence_num reconciliation

TickForge.App         Console host: adapters → sink → book → latency.
TickForge.Web         ASP.NET host + WebSocket + dashboard.
TickForge.Benchmarks  BenchmarkDotNet micro-benchmarks.
TickForge.Tests       xUnit: book, gap/resync, latency, parser parity.
```

Dependencies flow one way, inward: everything looks at `Core`; `Core` looks at nothing.

### Data flow

```
exchange ──▶ feed adapter ──▶ IBookSink ──▶ OrderBook
                 │                              │
          (parse + recv ts)            LatencyRecorder ──▶ console / dashboard
```

The adapter is the only component that understands a specific wire format. It parses raw frames into a normalized batch of `LevelChange`s, captures a receive timestamp the moment the frame leaves the socket, and calls `OnSnapshot` / `OnDelta` / `OnResync` on the sink. The sink applies the span to the book and records processing latency.

### Book consistency

Both exchanges publish the **absolute** resting quantity at a price level, but they bootstrap and recover differently — and both live entirely inside their own adapters.

- **Binance** opens the `@depth` diff stream and buffers events, fetches the REST snapshot, drops stale buffered events (`u <= lastUpdateId`), and applies from the first event satisfying `U <= lastUpdateId + 1 <= u`. In steady state, each event's `U` must continue from the previous `u + 1`; a gap triggers a resync and re-bootstrap.
- **Coinbase** subscribes to `level2`, takes the first `snapshot` as the book, then applies `update`s. The connection's `sequence_num` is **global** (it counts the subscription ack too), so continuity is validated against every message while only `l2_data` messages mutate the book. A break in the sequence triggers a resync and re-subscribe.

> This subtlety is easy to get wrong: filtering to `l2_data` before checking the sequence makes the numbers look broken and produces a spurious resync loop. TickForge counts every message, and a regression test guards this.

### Latency measurement

TickForge measures **internal tick-to-process latency**: the time between a frame being read off the socket (`Clock.NowNanos()`, monotonic) and `OrderBook.Apply` returning — a single-clock, defensible metric.

It **deliberately does not report** exchange-to-local latency: the exchange event timestamp and the local clock aren't synchronized, so the difference would be dominated by clock skew, not real network/processing time.

`Clock` uses `Stopwatch.GetTimestamp()` (monotonic, sub-microsecond), never `DateTime`. `LatencyRecorder` wraps an HdrHistogram and uses `RecordValueWithExpectedInterval` to correct for **coordinated omission** — the bias where a stalled consumer fails to record the very long-tail samples the stall itself created.

For the full design rationale, see [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Performance

To reproduce locally: `dotnet run -c Release --project bench/TickForge.Benchmarks`.

| Benchmark | What it compares |
| --- | --- |
| Depth parsing | Hand-written `Utf8JsonReader` vs source-generated DTO parsing |
| `OrderBook.ApplyDelta` | Applying a delta batch to a deep book |
| `BookSide` decimal vs `long` ticks | `decimal` correctness vs scaled-integer speed trade-off |

The hand-written parser (used on the live path) writes directly into a reused buffer and allocates **zero bytes** per frame; the source-generated path produces `string[][]` plus a `string` per price/quantity, spending **~5.4 KB** — the dominant per-message cost on that path. The source-gen path is kept as the reference implementation in the benchmark and in a parity test.

---

## Getting started

**Requirement:** [.NET 10 SDK](https://dotnet.microsoft.com/download). The public market-data feeds used here require no API keys.

```bash
git clone https://github.com/orhanyarkin/TickForge.git
cd TickForge

dotnet build -c Release
dotnet test
```

### Console host

```bash
# Both exchanges, BTC/USDT, live top-of-book + p50/p99 once per second:
dotnet run -c Release --project src/TickForge.App -- --exchange all --symbol BTC/USDT

# Single exchange:
dotnet run -c Release --project src/TickForge.App -- --exchange binance --symbol BTC/USDT
```

### Web dashboard

```bash
dotnet run -c Release --project src/TickForge.Web
# open http://localhost:5055
```

A dark, "trading terminal"-themed UI publishes a fresh snapshot ~10 times per second over WebSocket: per-exchange order book ladders with depth bars, best bid/ask/spread/mid and live latency chips. `GET /api/state` serves the same data as JSON. The symbol and exchanges can be configured via `TICKFORGE_SYMBOL` and `TICKFORGE_EXCHANGES`.

### Docker

```bash
docker compose -f infra/docker-compose.yml up --build
# open http://localhost:5055
```

The image is multi-stage (SDK build → `aspnet:10.0` runtime) and runs as a non-root user.

---

## Project structure

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

## Tests

```bash
dotnet test
```

The suite covers the places most likely to break:

- **Order book** — snapshot replace, delta apply, zero-quantity removal, best bid/ask selection, spread/mid math, empty-book edge cases, version monotonicity.
- **Sequencing** — Binance gap detection and bootstrap-boundary selection; Coinbase broken `sequence_num` (including the "other messages advance the counter but must not create a spurious gap" case found in live testing).
- **Latency** — percentile correctness on a known distribution, and that coordinated-omission correction actually inflates the tail during a stall.
- **Parser parity** — the hand-written parser produces output identical to the source-generated path.

Feed adapters are tested without a live socket: the reconcilers are pure and the snapshot source is an interface, so sequence scenarios are driven deterministically with in-memory data.

---

## Design decisions and trade-offs

- **`decimal` for price/quantity.** Correct and readable, but slower than scaled `long` ticks. The benchmark project measures the difference; production keeps `decimal` and treats the integer variant as an experiment.
- **`SortedDictionary` per book side.** Chosen for clarity. For dense books, a flat price-indexed array is faster and is the natural next benchmark.
- **Allocation-free hand-written parser on the live path (both exchanges).** The source-generated path is kept as the reference in a parity test proving the hand-rolled parser's output is correct, and in the benchmark.
- **What we don't measure.** Exchange-to-local latency. The exchange timestamp and the local clock aren't synchronized, so reporting the difference wouldn't be honest.
