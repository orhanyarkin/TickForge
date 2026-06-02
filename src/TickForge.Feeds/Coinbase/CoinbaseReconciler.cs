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

    /// <summary>Feed one normalized message (book or otherwise).</summary>
    public void OnMessage(in CoinbaseL2Message message, long recvTsNanos)
    {
        if (message.Kind == CoinbaseMessageKind.Snapshot)
        {
            _sink.OnSnapshot(_instrument, message.Levels, recvTsNanos);
            _expectedSequence = message.SequenceNum + 1;
            _synced = true;
            return;
        }

        if (!_synced)
            return; // ignore everything until the first snapshot establishes the book

        if (message.SequenceNum != _expectedSequence)
        {
            Resync($"sequence gap: expected {_expectedSequence}, got {message.SequenceNum}");
            return;
        }

        _expectedSequence = message.SequenceNum + 1;

        // Only book messages change the book; Other messages just advance the counter.
        if (message.Kind == CoinbaseMessageKind.Update)
            _sink.OnDelta(_instrument, message.Levels, recvTsNanos);
    }

    private void Resync(string reason)
    {
        _sink.OnResync(_instrument, reason);
        _synced = false;
        _expectedSequence = 0;
        _requestResync();
    }
}
