namespace TickForge.Core.Abstractions;

/// <summary>
/// A single normalized price-level change.
///
/// Both Binance and Coinbase publish the <b>absolute</b> resting quantity at a
/// price level (not a delta to add). A <see cref="Quantity"/> of zero means the
/// level should be removed from the book. Every adapter normalizes to this shape
/// so the <c>OrderBook</c> never needs to know which exchange produced it.
/// </summary>
public readonly record struct LevelChange(Side Side, decimal Price, decimal Quantity)
{
    /// <summary>True when this change removes the level.</summary>
    public bool IsRemoval => Quantity == 0m;
}
