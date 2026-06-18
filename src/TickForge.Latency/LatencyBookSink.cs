using System;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using TickForge.Core.Time;

namespace TickForge.Latency;

/// <summary>
/// The sink that closes the loop: it applies normalized level changes to an
/// <see cref="OrderBook"/> and records the internal tick-to-process latency —
/// the interval from when the frame was read off the socket (<c>recvTsNanos</c>)
/// to when the apply returns.
///
/// The order book is not thread-safe, so book writes (apply) and reads
/// (<see cref="ReadTop"/>) are serialized under one lock; a reader (e.g. a
/// once-a-second console printer on another thread) never sees a torn book or
/// races a <c>SortedDictionary</c> mutation. The latency histogram is concurrent,
/// so recording and <see cref="Snapshot"/> stay outside the lock.
/// </summary>
public sealed class LatencyBookSink : IBookSink
{
    private readonly object _gate = new();
    private readonly OrderBook _book;
    private readonly LatencyRecorder _latency;
    private readonly Action<string>? _log;

    public LatencyBookSink(OrderBook book, LatencyRecorder latency, Action<string>? log = null)
    {
        _book = book;
        _latency = latency;
        _log = log;
    }

    public void OnSnapshot(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        lock (_gate)
            _book.ApplySnapshot(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnDelta(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        lock (_gate)
            _book.ApplyDelta(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnResync(InstrumentId instrument, string reason) =>
        _log?.Invoke($"[resync] {instrument}: {reason}");

    /// <summary>A consistent top-of-book read, taken under the book lock.</summary>
    public BookTop ReadTop()
    {
        lock (_gate)
            return new BookTop(_book.Version, _book.BestBid, _book.BestAsk, _book.Spread, _book.Mid);
    }

    /// <summary>The current latency percentile snapshot (lock-free; histogram is concurrent).</summary>
    public LatencySnapshot Snapshot() => _latency.Snapshot();
}

/// <summary>An immutable top-of-book reading.</summary>
public readonly record struct BookTop(
    long Version,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    decimal? Mid);
