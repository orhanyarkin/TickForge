using System.Collections.Generic;
using TickForge.Core.Abstractions;

namespace TickForge.Benchmarks;

/// <summary>
/// Experimental scaled-integer variant of <c>BookSide</c>: prices and quantities
/// are pre-scaled to <see langword="long"/> ticks rather than <see langword="decimal"/>.
/// Kept in the benchmark project (not production) to quantify the decimal-vs-long
/// trade-off noted in the architecture doc — <see langword="decimal"/> is the
/// correct, readable choice for production; <see langword="long"/> ticks trade
/// that for speed.
/// </summary>
public sealed class ScaledBookSide
{
    private readonly SortedDictionary<long, long> _levels;

    public ScaledBookSide(Side side)
    {
        IComparer<long> comparer = side == Side.Bid
            ? Comparer<long>.Create(static (a, b) => b.CompareTo(a))
            : Comparer<long>.Default;
        _levels = new SortedDictionary<long, long>(comparer);
    }

    public int Count => _levels.Count;

    public void Apply(long priceTicks, long quantityTicks)
    {
        if (quantityTicks == 0)
            _levels.Remove(priceTicks);
        else
            _levels[priceTicks] = quantityTicks;
    }

    public bool TryGetBest(out long priceTicks, out long quantityTicks)
    {
        foreach (var kv in _levels)
        {
            priceTicks = kv.Key;
            quantityTicks = kv.Value;
            return true;
        }

        priceTicks = 0;
        quantityTicks = 0;
        return false;
    }
}
