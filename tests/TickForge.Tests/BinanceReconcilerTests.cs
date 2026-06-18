using TickForge.Core.Abstractions;
using TickForge.Feeds.Binance;
using Xunit;

namespace TickForge.Tests;

public sealed class BinanceReconcilerTests
{
    private static readonly InstrumentId Btc = InstrumentId.Parse("BTC/USDT");
    private const long Ts = 1_000;

    private static LevelChange Bid(decimal price, decimal qty) => new(Side.Bid, price, qty);
    private static LevelChange Ask(decimal price, decimal qty) => new(Side.Ask, price, qty);

    private static LevelChange[] Levels(params LevelChange[] levels) => levels;

    private static BinanceSnapshot Snap(long lastUpdateId, params LevelChange[] levels) =>
        new(lastUpdateId, levels);

    [Fact]
    public void HappyPath_AppliesSnapshotThenBufferedAndLiveDiffs_NoResync()
    {
        var sink = new FakeBookSink();
        var snapshotRequests = 0;
        var rec = new BinanceReconciler(Btc, sink, () => snapshotRequests++);

        rec.Reset();                                                  // request snapshot, start buffering
        rec.OnDiff(101, 105, Levels(Bid(100m, 1m)), Ts);             // buffered, applies at boundary
        rec.OnSnapshotReceived(Snap(100, Bid(100m, 5m), Ask(101m, 5m)), Ts);
        rec.OnDiff(106, 110, Levels(Ask(101m, 0m)), Ts);             // live, contiguous: removes the ask

        Assert.Empty(sink.Resyncs);
        Assert.Equal(1, snapshotRequests);
        Assert.Single(sink.Snapshots);
        Assert.Equal(2, sink.Deltas.Count);                          // buffered replay + live diff
        Assert.Equal(100m, sink.Book.BestBid);                       // overwritten 5 -> 1
        Assert.Equal(1m, sink.Book.BestBidQuantity);
        Assert.Null(sink.Book.BestAsk);                              // removed by the live diff
    }

    [Fact]
    public void SteadyStateGap_TriggersResyncAndRequestsSnapshot()
    {
        var sink = new FakeBookSink();
        var snapshotRequests = 0;
        var rec = new BinanceReconciler(Btc, sink, () => snapshotRequests++);

        rec.Reset();                                                 // request #1
        rec.OnSnapshotReceived(Snap(100, Bid(100m, 1m)), Ts);
        rec.OnDiff(101, 105, Levels(Bid(100m, 2m)), Ts);            // contiguous
        rec.OnDiff(110, 115, Levels(Bid(100m, 3m)), Ts);            // hole: U=110 > 105+1

        Assert.Single(sink.Resyncs);
        Assert.Equal(2, snapshotRequests);                           // reset + resync
    }

    [Fact]
    public void StaleBufferedEvents_AreDropped()
    {
        var sink = new FakeBookSink();
        var rec = new BinanceReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnDiff(50, 90, Levels(Bid(100m, 9m)), Ts);             // u=90 <= lastUpdateId 100 -> stale
        rec.OnDiff(101, 105, Levels(Bid(100m, 1m)), Ts);           // applicable
        rec.OnSnapshotReceived(Snap(100, Bid(100m, 5m)), Ts);

        Assert.Empty(sink.Resyncs);
        Assert.Single(sink.Deltas);                                 // only the in-window event applied
        Assert.Equal(1m, sink.Book.BestBidQuantity);
    }

    [Fact]
    public void BootstrapBoundaryGap_TriggersResync()
    {
        var sink = new FakeBookSink();
        var snapshotRequests = 0;
        var rec = new BinanceReconciler(Btc, sink, () => snapshotRequests++);

        rec.Reset();                                                // request #1
        rec.OnDiff(103, 108, Levels(Bid(100m, 1m)), Ts);           // first event U=103 > lastUpdateId+1=101
        rec.OnSnapshotReceived(Snap(100, Bid(100m, 5m)), Ts);

        Assert.Single(sink.Resyncs);
        Assert.Equal(2, snapshotRequests);
    }

    [Fact]
    public void FirstEventExactlyAtBoundary_IsApplied()
    {
        var sink = new FakeBookSink();
        var rec = new BinanceReconciler(Btc, sink, () => { });

        rec.Reset();
        rec.OnSnapshotReceived(Snap(100, Bid(100m, 5m)), Ts);
        rec.OnDiff(99, 101, Levels(Bid(100m, 7m)), Ts);            // U=99 <= 101 <= u=101 -> apply

        Assert.Empty(sink.Resyncs);
        Assert.Single(sink.Deltas);
        Assert.Equal(7m, sink.Book.BestBidQuantity);
    }
}
