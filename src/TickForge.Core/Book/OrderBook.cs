using System;
using TickForge.Core.Abstractions;

namespace TickForge.Core.Book;

/// <summary>
/// An exchange-agnostic L2 order book. It knows only the normalized
/// <see cref="LevelChange"/> shape and never branches on exchange identity —
/// all wire-specific consistency logic lives in the feed adapters.
///
/// The hot path is span-based and free of LINQ so a feed adapter can hand it a
/// pooled buffer without per-message heap allocation.
/// </summary>
public sealed class OrderBook
{
    private readonly BookSide _bids = new(Side.Bid);
    private readonly BookSide _asks = new(Side.Ask);

    /// <summary>
    /// Monotonically increasing revision, bumped on every snapshot or delta.
    /// Lets consumers detect that the book moved without diffing it.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>Distinct resting bid levels.</summary>
    public int BidCount => _bids.Count;

    /// <summary>Distinct resting ask levels.</summary>
    public int AskCount => _asks.Count;

    /// <summary>
    /// Replace the entire book with <paramref name="levels"/> (initial sync or a
    /// post-gap resync). Both sides are cleared first.
    /// </summary>
    public void ApplySnapshot(ReadOnlySpan<LevelChange> levels)
    {
        _bids.Clear();
        _asks.Clear();
        ApplyLevels(levels);
        Version++;
    }

    /// <summary>Apply incremental changes on top of the current book.</summary>
    public void ApplyDelta(ReadOnlySpan<LevelChange> levels)
    {
        ApplyLevels(levels);
        Version++;
    }

    private void ApplyLevels(ReadOnlySpan<LevelChange> levels)
    {
        foreach (ref readonly var level in levels)
        {
            var side = level.Side == Side.Bid ? _bids : _asks;
            side.Apply(level.Price, level.Quantity);
        }
    }

    /// <summary>Highest resting bid price, or <c>null</c> when there are no bids.</summary>
    public decimal? BestBid => _bids.TryGetBest(out var price, out _) ? price : null;

    /// <summary>Lowest resting ask price, or <c>null</c> when there are no asks.</summary>
    public decimal? BestAsk => _asks.TryGetBest(out var price, out _) ? price : null;

    /// <summary>Resting quantity at the best bid, or <c>null</c> when there are no bids.</summary>
    public decimal? BestBidQuantity => _bids.TryGetBest(out _, out var qty) ? qty : null;

    /// <summary>Resting quantity at the best ask, or <c>null</c> when there are no asks.</summary>
    public decimal? BestAskQuantity => _asks.TryGetBest(out _, out var qty) ? qty : null;

    /// <summary>Best ask minus best bid, or <c>null</c> when either side is empty.</summary>
    public decimal? Spread =>
        BestBid is { } bid && BestAsk is { } ask ? ask - bid : null;

    /// <summary>Midpoint of the best bid and ask, or <c>null</c> when either side is empty.</summary>
    public decimal? Mid =>
        BestBid is { } bid && BestAsk is { } ask ? (bid + ask) / 2m : null;
}
