using System;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Coinbase;

/// <summary>
/// The Coinbase book-consistency state machine — pure and I/O-free so it is
/// unit-testable without a socket. Coinbase is snapshot-first: the first
/// <c>l2_data</c> message is a <c>snapshot</c>, the rest are <c>update</c>s.
///
/// The <c>sequence_num</c> is global across every message on the connection
/// (including the subscriptions ack), not per-channel, so continuity is checked
/// against <b>all</b> messages while only <c>l2_data</c> changes the book. The
/// snapshot establishes the baseline; any later break in <c>sequence_num</c>
/// means a message was lost → emit <see cref="IBookSink.OnResync"/> and ask the
/// transport to re-subscribe (which yields a fresh snapshot).
/// </summary>
public sealed class CoinbaseReconciler
{
    private readonly InstrumentId _instrument;
    private readonly IBookSink _sink;
    private readonly Action _requestResync;

    private bool _synced;
    private long _expectedSequence;

    public CoinbaseReconciler(InstrumentId instrument, IBookSink sink, Action requestResync)
    {
        _instrument = instrument;
        _sink = sink;
        _requestResync = requestResync;
    }

    /// <summary>Called when the transport (re)connects: drop state and await a fresh snapshot.</summary>
    public void Reset()
    {
        _synced = false;
        _expectedSequence = 0;
    }

    /// <summary>
    /// Feed one message. <paramref name="levels"/> is a span over the adapter's
    /// reused parse buffer; the Coinbase reconciler never buffers, so the span is
    /// passed straight through to the sink with no copy.
    /// </summary>
    public void OnMessage(
        long sequenceNum, CoinbaseMessageKind kind, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        if (kind == CoinbaseMessageKind.Snapshot)
        {
            _sink.OnSnapshot(_instrument, levels, recvTsNanos);
            _expectedSequence = sequenceNum + 1;
            _synced = true;
            return;
        }

        if (!_synced)
            return; // ignore everything until the first snapshot establishes the book

        if (sequenceNum != _expectedSequence)
        {
            Resync($"sequence gap: expected {_expectedSequence}, got {sequenceNum}");
            return;
        }

        _expectedSequence = sequenceNum + 1;

        // Only book messages change the book; Other messages just advance the counter.
        if (kind == CoinbaseMessageKind.Update)
            _sink.OnDelta(_instrument, levels, recvTsNanos);
    }

    private void Resync(string reason)
    {
        _sink.OnResync(_instrument, reason);
        _synced = false;
        _expectedSequence = 0;
        _requestResync();
    }
}
