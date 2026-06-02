using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using Xunit;

namespace TickForge.Tests;

public sealed class OrderBookTests
{
    private static LevelChange Bid(decimal price, decimal qty) => new(Side.Bid, price, qty);
    private static LevelChange Ask(decimal price, decimal qty) => new(Side.Ask, price, qty);

    [Fact]
    public void EmptyBook_HasNoBestOrSpread()
    {
        var book = new OrderBook();

        Assert.Null(book.BestBid);
        Assert.Null(book.BestAsk);
        Assert.Null(book.Spread);
        Assert.Null(book.Mid);
        Assert.Equal(0, book.Version);
    }

    [Fact]
    public void Snapshot_PopulatesBestBidAndAsk()
    {
        var book = new OrderBook();

        book.ApplySnapshot([Bid(100m, 1m), Bid(99m, 2m), Ask(101m, 1m), Ask(102m, 3m)]);

        Assert.Equal(100m, book.BestBid);
        Assert.Equal(101m, book.BestAsk);
        Assert.Equal(1m, book.Spread);
        Assert.Equal(100.5m, book.Mid);
        Assert.Equal(2, book.BidCount);
        Assert.Equal(2, book.AskCount);
        Assert.Equal(1, book.Version);
    }

    [Fact]
    public void Snapshot_ReplacesPreviousBook()
    {
        var book = new OrderBook();
        book.ApplySnapshot([Bid(100m, 1m), Ask(101m, 1m)]);

        book.ApplySnapshot([Bid(200m, 5m), Ask(201m, 5m)]);

        Assert.Equal(200m, book.BestBid);
        Assert.Equal(201m, book.BestAsk);
        Assert.Equal(1, book.BidCount);
        Assert.Equal(1, book.AskCount);
    }

    [Fact]
    public void Delta_UpdatesAndAddsLevels()
    {
        var book = new OrderBook();
        book.ApplySnapshot([Bid(100m, 1m), Ask(101m, 1m)]);

        book.ApplyDelta([Bid(100m, 4m), Bid(101m, 2m)]);

        Assert.Equal(101m, book.BestBid);
        Assert.Equal(2m, book.BestBidQuantity);
        Assert.Equal(2, book.BidCount);
        Assert.Equal(2, book.Version);
    }

    [Fact]
    public void Delta_WithZeroQuantity_RemovesLevel()
    {
        var book = new OrderBook();
        book.ApplySnapshot([Bid(100m, 1m), Bid(99m, 2m), Ask(101m, 1m)]);

        book.ApplyDelta([Bid(100m, 0m)]);

        Assert.Equal(99m, book.BestBid);
        Assert.Equal(1, book.BidCount);
    }

    [Fact]
    public void Removing_AllOfOneSide_LeavesNoSpread()
    {
        var book = new OrderBook();
        book.ApplySnapshot([Bid(100m, 1m), Ask(101m, 1m)]);

        book.ApplyDelta([Ask(101m, 0m)]);

        Assert.Equal(100m, book.BestBid);
        Assert.Null(book.BestAsk);
        Assert.Null(book.Spread);
        Assert.Null(book.Mid);
    }

    [Fact]
    public void BestBid_IsHighest_BestAsk_IsLowest_RegardlessOfInsertionOrder()
    {
        var book = new OrderBook();

        book.ApplySnapshot([Bid(98m, 1m), Bid(100m, 1m), Bid(99m, 1m),
            Ask(103m, 1m), Ask(101m, 1m), Ask(102m, 1m)]);

        Assert.Equal(100m, book.BestBid);
        Assert.Equal(101m, book.BestAsk);
    }

    [Fact]
    public void Version_IncrementsMonotonically()
    {
        var book = new OrderBook();

        book.ApplySnapshot([Bid(100m, 1m)]);
        book.ApplyDelta([Bid(100m, 2m)]);
        book.ApplyDelta([Bid(100m, 3m)]);

        Assert.Equal(3, book.Version);
    }
}
