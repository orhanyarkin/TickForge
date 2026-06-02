using System;
using System.Collections.Generic;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Binance;

/// <summary>
/// The Binance book-consistency state machine — pure and I/O-free so it can be
/// unit-tested without a socket or HTTP. It implements the canonical Binance
/// bootstrap and steady-state sequencing from the architecture doc:
///
/// <list type="number">
/// <item>Diff events arriving before the snapshot are buffered.</item>
/// <item>On the snapshot, replace the book and note its <c>lastUpdateId</c>.</item>
/// <item>Drop buffered events whose <c>u &lt;= lastUpdateId</c>.</item>
/// <item>The first event to apply must satisfy <c>U &lt;= lastUpdateId+1 &lt;= u</c>.</item>
/// <item>Steady state: each event's <c>U</c> must continue from the previous
/// <c>u</c>; a hole means a message was lost → resync.</item>
/// </list>
///
/// All sequencing emits through an <see cref="IBookSink"/>. When a resync is
/// required the reconciler invokes the supplied <c>requestSnapshot</c> callback
/// so the transport can re-fetch the REST snapshot.
/// </summary>
public sealed class BinanceReconciler
{
    private readonly InstrumentId _instrument;
    private readonly IBookSink _sink;
    private readonly Action _requestSnapshot;
    private readonly List<BufferedEvent> _buffer = [];

    private bool _live;
    private long _lastFinalId;

    public BinanceReconciler(InstrumentId instrument, IBookSink sink, Action requestSnapshot)
    {
        _instrument = instrument;
        _sink = sink;
        _requestSnapshot = requestSnapshot;
    }

    /// <summary>
    /// Called when the transport (re)connects: clear state, start buffering, and
    /// ask for a fresh snapshot.
    /// </summary>
    public void Reset()
    {
        _live = false;
        _lastFinalId = 0;
        _buffer.Clear();
        _requestSnapshot();
    }

    /// <summary>Feed one diff event. Before the snapshot it is buffered; after, it is sequenced.</summary>
    public void OnDiff(in BinanceDepthEvent evt, long recvTsNanos)
    {
        if (!_live)
        {
            _buffer.Add(new BufferedEvent(evt, recvTsNanos));
            return;
        }

        ApplyLive(evt, recvTsNanos);
    }

    /// <summary>Apply the REST snapshot, then replay any buffered diffs on top of it.</summary>
    public void OnSnapshotReceived(in BinanceSnapshot snapshot, long recvTsNanos)
    {
        _sink.OnSnapshot(_instrument, snapshot.Levels, recvTsNanos);
        _lastFinalId = snapshot.LastUpdateId;
        _live = true;

        // Snapshot the pending diffs and clear the buffer up front so a resync
        // triggered mid-replay can safely reset state without mutating what we
        // are iterating. Stale events (u <= lastUpdateId) are dropped in
        // ApplyLive; a hole at the boundary triggers a resync.
        var pending = _buffer.ToArray();
        _buffer.Clear();

        foreach (var buffered in pending)
        {
            if (!_live)
                break; // a replayed event triggered a resync; stop draining

            ApplyLive(buffered.Event, buffered.RecvTsNanos);
        }
    }

    private void ApplyLive(in BinanceDepthEvent evt, long recvTsNanos)
    {
        if (evt.FinalUpdateId <= _lastFinalId)
            return; // fully-seen or stale event

        if (evt.FirstUpdateId <= _lastFinalId + 1)
        {
            _sink.OnDelta(_instrument, evt.Levels, recvTsNanos);
            _lastFinalId = evt.FinalUpdateId;
            return;
        }

        // evt.FirstUpdateId > _lastFinalId + 1 → a message was lost.
        Resync($"sequence gap: expected U<={_lastFinalId + 1}, got U={evt.FirstUpdateId}");
    }

    private void Resync(string reason)
    {
        _sink.OnResync(_instrument, reason);
        _live = false;
        _lastFinalId = 0;
        _buffer.Clear();
        _requestSnapshot();
    }

    private readonly struct BufferedEvent(BinanceDepthEvent evt, long recvTsNanos)
    {
        public BinanceDepthEvent Event { get; } = evt;
        public long RecvTsNanos { get; } = recvTsNanos;
    }
}
