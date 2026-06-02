using System;
using System.Globalization;
using System.Text.Json;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Coinbase;

/// <summary>What a parsed Coinbase envelope carries.</summary>
public enum CoinbaseMessageKind
{
    /// <summary>A non-book message (e.g. the subscriptions ack) — counts only for sequencing.</summary>
    Other,

    /// <summary>The initial level2 snapshot.</summary>
    Snapshot,

    /// <summary>An incremental level2 update.</summary>
    Update,
}

/// <summary>
/// A normalized Coinbase message: its <c>sequence_num</c>, its kind, and (for
/// book messages) the level changes (<c>bid</c>/<c>offer</c> mapped to
/// <see cref="Side"/>; <c>new_quantity == 0</c> is a removal).
///
/// Every message on the connection carries a <c>sequence_num</c>, so non-book
/// messages are kept (as <see cref="CoinbaseMessageKind.Other"/>) to preserve
/// sequence continuity — the counter is global, not per-channel.
/// </summary>
public readonly struct CoinbaseL2Message
{
    public CoinbaseL2Message(long sequenceNum, CoinbaseMessageKind kind, LevelChange[] levels)
    {
        SequenceNum = sequenceNum;
        Kind = kind;
        Levels = levels;
    }

    public long SequenceNum { get; }
    public CoinbaseMessageKind Kind { get; }
    public LevelChange[] Levels { get; }
}

/// <summary>Renders a canonical <see cref="InstrumentId"/> into Coinbase wire format.</summary>
public static class CoinbaseSymbols
{
    /// <summary>Coinbase product id, e.g. <c>BTC-USDT</c>.</summary>
    public static string ToProductId(InstrumentId instrument) =>
        $"{instrument.Base}-{instrument.Quote}";
}

/// <summary>Parses Coinbase frames into the normalized <see cref="CoinbaseL2Message"/>.</summary>
internal static class CoinbaseParsing
{
    public static bool TryParse(ReadOnlySpan<byte> payload, out CoinbaseL2Message message)
    {
        var envelope = JsonSerializer.Deserialize(payload, CoinbaseJsonContext.Default.CoinbaseL2Envelope);
        return TryFromEnvelope(envelope, out message);
    }

    public static bool TryFromEnvelope(CoinbaseL2Envelope? envelope, out CoinbaseL2Message message)
    {
        message = default;
        if (envelope is null)
            return false;

        // Non-book messages (subscriptions ack, etc.) still advance the global
        // sequence_num, so keep them as Other to preserve continuity.
        if (envelope.Channel != "l2_data" || envelope.Events is null || envelope.Events.Length == 0)
        {
            message = new CoinbaseL2Message(envelope.SequenceNum, CoinbaseMessageKind.Other, []);
            return true;
        }

        var kind = CoinbaseMessageKind.Update;
        var total = 0;
        foreach (var ev in envelope.Events)
        {
            if (string.Equals(ev.Type, "snapshot", StringComparison.Ordinal))
                kind = CoinbaseMessageKind.Snapshot;
            if (ev.Updates is not null)
                total += ev.Updates.Length;
        }

        var levels = new LevelChange[total];
        var count = 0;
        foreach (var ev in envelope.Events)
        {
            if (ev.Updates is null)
                continue;

            foreach (var update in ev.Updates)
            {
                if (update.PriceLevel is null || update.NewQuantity is null)
                    continue;

                var side = string.Equals(update.Side, "bid", StringComparison.Ordinal)
                    ? Side.Bid
                    : Side.Ask;
                var price = decimal.Parse(update.PriceLevel, CultureInfo.InvariantCulture);
                var quantity = decimal.Parse(update.NewQuantity, CultureInfo.InvariantCulture);
                levels[count++] = new LevelChange(side, price, quantity);
            }
        }

        if (count != levels.Length)
            Array.Resize(ref levels, count);

        message = new CoinbaseL2Message(envelope.SequenceNum, kind, levels);
        return true;
    }
}
