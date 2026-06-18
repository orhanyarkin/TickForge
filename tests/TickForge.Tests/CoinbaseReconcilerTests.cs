using TickForge.Core.Abstractions;
using TickForge.Feeds.Coinbase;
using Xunit;
using static TickForge.Feeds.Coinbase.CoinbaseMessageKind;

namespace TickForge.Tests;

public sealed class CoinbaseReconcilerTests
{
    private static readonly InstrumentId Btc = InstrumentId.Parse("BTC/USDT");
    private const long Ts = 1_000;

    private static LevelChange Bid(decimal price, decimal qty) => new(Side.Bid, price, qty);
    private static LevelChange Ask(decimal price, decimal qty) => new(Side.Ask, price, qty);
    private static LevelChange[] Levels(params LevelChange[] levels) => levels;

    [Fact]
    public void SnapshotThenContiguousUpdates_NoResync()
    {
        var sink = new FakeBookSink();
        var resyncRequests = 0;
        var rec = new CoinbaseReconciler(Btc, sink, () => resyncRequests++);

        rec.Reset();
        rec.OnMessage(5, Snapshot, Levels(Bid(100m, 1m), Ask(101m, 1m)), Ts);
        rec.OnMessage(6, Update, Levels(Bid(100m, 2m)), Ts);
        rec.OnMessage(7, Update, Levels(Ask(101m, 0m)), Ts);   // removes the ask

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
        rec.OnMessage(0, Snapshot, Levels(Bid(100m, 1m)), Ts);   // expected 1
        rec.OnMessage(1, Other, Levels(), Ts);                   // ack consumes seq 1 -> expected 2
        rec.OnMessage(2, Update, Levels(Bid(100m, 2m)), Ts);     // seq 2 matches -> applied

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
        rec.OnMessage(5, Snapshot, Levels(Bid(100m, 1m)), Ts);
        rec.OnMessage(6, Update, Levels(Bid(100m, 2m)), Ts);
        rec.OnMessage(9, Update, Levels(Bid(100m, 3m)), Ts);     // gap: expected 7, got 9

        Assert.Single(sink.Resyncs);
        Assert.Equal(1, resyncRequests);
    }

    [Fact]
    public void UpdatesBeforeSnapshot_AreIgnored()
    {
        var sink = new FakeBookSink();
        var rec = new CoinbaseReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnMessage(3, Update, Levels(Bid(100m, 1m)), Ts);     // no snapshot yet

        Assert.Empty(sink.Deltas);
        Assert.Empty(sink.Resyncs);
    }

    [Fact]
    public void SnapshotAfterResync_ReestablishesBaseline()
    {
        var sink = new FakeBookSink();
        var rec = new CoinbaseReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnMessage(5, Snapshot, Levels(Bid(100m, 1m)), Ts);
        rec.OnMessage(9, Update, Levels(Bid(100m, 2m)), Ts);     // gap -> resync, awaits a fresh snapshot

        // The re-subscribe yields a new snapshot with a new sequence baseline.
        rec.OnMessage(20, Snapshot, Levels(Bid(200m, 5m), Ask(201m, 5m)), Ts);
        rec.OnMessage(21, Update, Levels(Bid(200m, 7m)), Ts);

        Assert.Single(sink.Resyncs);
        Assert.Equal(2, sink.Snapshots.Count);
        Assert.Equal(200m, sink.Book.BestBid);
        Assert.Equal(7m, sink.Book.BestBidQuantity);
    }
}
