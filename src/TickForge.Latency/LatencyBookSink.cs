using System;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using TickForge.Core.Time;

namespace TickForge.Latency;

/// <summary>
/// The sink that closes the loop: it applies normalized level changes to an
/// <see cref="OrderBook"/> and records the internal tick-to-process latency —
/// the interval from when the frame was read off the socket
/// (<c>recvTsNanos</c>) to when the apply returns.
/// </summary>
public sealed class LatencyBookSink : IBookSink
{
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
        _book.ApplySnapshot(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnDelta(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        _book.ApplyDelta(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnResync(InstrumentId instrument, string reason) =>
        _log?.Invoke($"[resync] {instrument}: {reason}");
}
