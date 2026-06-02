namespace TickForge.Core.Abstractions;

/// <summary>
/// Exchange-agnostic instrument identifier. Each exchange formats symbols
/// differently (Binance: <c>BTCUSDT</c>, Coinbase: <c>BTC-USD</c>); we keep a
/// canonical (Base, Quote) pair and let each adapter render the wire format.
/// </summary>
public readonly record struct InstrumentId(string Base, string Quote)
{
    /// <summary>Canonical form, e.g. <c>BTC/USDT</c>.</summary>
    public string Canonical => $"{Base}/{Quote}";

    /// <summary>Parse a canonical "BASE/QUOTE" string.</summary>
    public static InstrumentId Parse(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        var slash = canonical.IndexOf('/');
        if (slash <= 0 || slash == canonical.Length - 1)
            throw new FormatException($"Expected 'BASE/QUOTE', got '{canonical}'.");

        return new InstrumentId(
            canonical[..slash].Trim().ToUpperInvariant(),
            canonical[(slash + 1)..].Trim().ToUpperInvariant());
    }

    public override string ToString() => Canonical;
}
