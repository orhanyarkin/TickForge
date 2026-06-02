namespace TickForge.Core.Abstractions;

/// <summary>
/// The contract a feed adapter pushes into. Designed around
/// <see cref="ReadOnlySpan{T}"/> so the hot path stays allocation-free: the
/// adapter parses into a pooled buffer and hands a span to the sink, which
/// applies it to the book before the buffer is reused.
///
/// Timestamps are monotonic nanoseconds from <c>Clock.NowNanos()</c>, captured
/// by the adapter the instant a frame is read off the socket — this is the
/// reference point for internal tick-to-process latency.
/// </summary>
public interface IBookSink
{
    /// <summary>A full book replacement (initial sync or post-gap resync).</summary>
    void OnSnapshot(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos);

    /// <summary>An incremental update applied on top of the current book.</summary>
    void OnDelta(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos);

    /// <summary>
    /// A recoverable inconsistency was detected (sequence gap, stale snapshot).
    /// The adapter will follow with a fresh <see cref="OnSnapshot"/>.
    /// </summary>
    void OnResync(InstrumentId instrument, string reason);
}
