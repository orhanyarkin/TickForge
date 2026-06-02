using System.Collections.Generic;
using TickForge.Core.Abstractions;

namespace TickForge.Core.Book;

/// <summary>
/// One side (bids or asks) of an L2 order book, keyed by price.
///
/// Backed by a <see cref="SortedDictionary{TKey,TValue}"/> whose comparer is
/// chosen so the <b>first</b> entry is always the best level: descending price
/// for bids (highest first), ascending for asks (lowest first). This keeps
/// best-of-book reads to the first enumerated element while staying easy to read.
///
/// <see cref="SortedDictionary{TKey,TValue}"/> is chosen for clarity per the
/// architecture notes; a flat price-indexed array is faster for dense books and
/// is benchmarked in a later phase.
/// </summary>
public sealed class BookSide
{
    private readonly SortedDictionary<decimal, decimal> _levels;

    public BookSide(Side side)
    {
        Side = side;
        // Bids: highest price is best -> descending. Asks: lowest price is best -> ascending.
        IComparer<decimal> comparer = side == Side.Bid
            ? Comparer<decimal>.Create(static (a, b) => b.CompareTo(a))
            : Comparer<decimal>.Default;
        _levels = new SortedDictionary<decimal, decimal>(comparer);
    }

    /// <summary>Which side of the book this is.</summary>
    public Side Side { get; }

    /// <summary>Number of distinct price levels currently resting.</summary>
    public int Count => _levels.Count;

    /// <summary>Remove every level.</summary>
    public void Clear() => _levels.Clear();

    /// <summary>
    /// Apply an absolute resting quantity at <paramref name="price"/>. A
    /// <paramref name="quantity"/> of zero removes the level.
    /// </summary>
    public void Apply(decimal price, decimal quantity)
    {
        if (quantity == 0m)
            _levels.Remove(price);
        else
            _levels[price] = quantity;
    }

    /// <summary>
    /// Copy the best levels (best first) into <paramref name="destination"/>,
    /// up to its length, and return how many were written. Intended for
    /// display/snapshotting, not the hot path; uses the struct enumerator so it
    /// does not allocate.
    /// </summary>
    public int CopyTop(Span<LevelChange> destination)
    {
        var count = 0;
        foreach (var kv in _levels)
        {
            if (count >= destination.Length)
                break;
            destination[count++] = new LevelChange(Side, kv.Key, kv.Value);
        }

        return count;
    }

    /// <summary>
    /// The best level on this side (highest bid / lowest ask), or <c>false</c>
    /// when the side is empty. Reads the first element via the struct enumerator,
    /// so it does not allocate.
    /// </summary>
    public bool TryGetBest(out decimal price, out decimal quantity)
    {
        foreach (var kv in _levels)
        {
            price = kv.Key;
            quantity = kv.Value;
            return true;
        }

        price = 0m;
        quantity = 0m;
        return false;
    }
}
