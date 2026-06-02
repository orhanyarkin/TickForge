using TickForge.Core.Abstractions;
using TickForge.Feeds.Coinbase;
using Xunit;

namespace TickForge.Tests;

public sealed class CoinbaseReconcilerTests
{
    private static readonly InstrumentId Btc = InstrumentId.Parse("BTC/USDT");
    private const long Ts = 1_000;

    private static LevelChange Bid(decimal price, decimal qty) => new(Side.Bid, price, qty);
    private static LevelChange Ask(decimal price, decimal qty) => new(Side.Ask, price, qty);

    private static CoinbaseL2Message Snap(long seq, params LevelChange[] levels) =>
        new(seq, CoinbaseMessageKind.Snapshot, levels);

    private static CoinbaseL2Message Upd(long seq, params LevelChange[] levels) =>
        new(seq, CoinbaseMessageKind.Update, levels);

    private static CoinbaseL2Message Other(long seq) =>
        new(seq, CoinbaseMessageKind.Other, []);

    [Fact]
    public void SnapshotThenContiguousUpdates_NoResync()
    {
        var sink = new FakeBookSink();
        var resyncRequests = 0;
        var rec = new CoinbaseReconciler(Btc, sink, () => resyncRequests++);

        rec.Reset();
        rec.OnMessage(Snap(5, Bid(100m, 1m), Ask(101m, 1m)), Ts);
        rec.OnMessage(Upd(6, Bid(100m, 2m)), Ts);
        rec.OnMessage(Upd(7, Ask(101m, 0m)), Ts);   // removes the ask

        Assert.Empty(sink.Resyncs);
        Assert.Equal(0, resyncRequests);
        Assert.Single(sink.Snapshots);
        Assert.Equal(2, sink.Deltas.Count);
        Assert.Equal(2m, sink.Book.BestBidQuantity);
        Assert.Null(sink.Book.BestAsk);
    }

    [Fact]
    public void OtherMessages_AdvanceSequence_WithoutFalseGap()
    {
        // Reproduces the real Coinbase wire: the subscriptions ack consumes a
        // global sequence number between the snapshot and the first update.
        var sink = new FakeBookSink();
        var resyncRequests = 0;
        var rec = new CoinbaseReconciler(Btc, sink, () => resyncRequests++);

        rec.Reset();
        rec.OnMessage(Snap(0, Bid(100m, 1m)), Ts);   // expected 1
        rec.OnMessage(Other(1), Ts);                 // ack consumes seq 1 -> expected 2
        rec.OnMessage(Upd(2, Bid(100m, 2m)), Ts);    // seq 2 matches -> applied

        Assert.Empty(sink.Resyncs);
        Assert.Equal(0, resyncRequests);
        Assert.Single(sink.Deltas);
        Assert.Equal(2m, sink.Book.BestBidQuantity);
    }

    [Fact]
    public void NonContiguousSequence_TriggersResyncAndRequestsResubscribe()
    {
        var sink = new FakeBookSink();
        var resyncRequests = 0;
        var rec = new CoinbaseReconciler(Btc, sink, () => resyncRequests++);

        rec.Reset();
        rec.OnMessage(Snap(5, Bid(100m, 1m)), Ts);
        rec.OnMessage(Upd(6, Bid(100m, 2m)), Ts);
        rec.OnMessage(Upd(9, Bid(100m, 3m)), Ts);   // gap: expected 7, got 9

        Assert.Single(sink.Resyncs);
        Assert.Equal(1, resyncRequests);
    }

    [Fact]
    public void UpdatesBeforeSnapshot_AreIgnored()
    {
        var sink = new FakeBookSink();
        var rec = new CoinbaseReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnMessage(Upd(3, Bid(100m, 1m)), Ts);   // no snapshot yet

        Assert.Empty(sink.Deltas);
        Assert.Empty(sink.Resyncs);
    }

    [Fact]
    public void SnapshotAfterResync_ReestablishesBaseline()
    {
        var sink = new FakeBookSink();
        var rec = new CoinbaseReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnMessage(Snap(5, Bid(100m, 1m)), Ts);
        rec.OnMessage(Upd(9, Bid(100m, 2m)), Ts);   // gap -> resync, awaits a fresh snapshot

        // The re-subscribe yields a new snapshot with a new sequence baseline.
        rec.OnMessage(Snap(20, Bid(200m, 5m), Ask(201m, 5m)), Ts);
        rec.OnMessage(Upd(21, Bid(200m, 7m)), Ts);

        Assert.Single(sink.Resyncs);
        Assert.Equal(2, sink.Snapshots.Count);
        Assert.Equal(200m, sink.Book.BestBid);
        Assert.Equal(7m, sink.Book.BestBidQuantity);
    }
}
