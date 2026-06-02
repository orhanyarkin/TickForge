using System;
using System.Globalization;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Binance;

/// <summary>
/// A normalized Binance depth diff event: the update-id window plus the level
/// changes (bids and asks already tagged with their <see cref="Side"/>).
/// </summary>
public readonly struct BinanceDepthEvent
{
    public BinanceDepthEvent(long firstUpdateId, long finalUpdateId, LevelChange[] levels)
    {
        FirstUpdateId = firstUpdateId;
        FinalUpdateId = finalUpdateId;
        Levels = levels;
    }

    /// <summary>Binance <c>U</c> — the first update id covered by this event.</summary>
    public long FirstUpdateId { get; }

    /// <summary>Binance <c>u</c> — the final update id covered by this event.</summary>
    public long FinalUpdateId { get; }

    /// <summary>The normalized level changes carried by this event.</summary>
    public LevelChange[] Levels { get; }
}

/// <summary>A normalized REST depth snapshot: its <c>lastUpdateId</c> and levels.</summary>
public readonly struct BinanceSnapshot
{
    public BinanceSnapshot(long lastUpdateId, LevelChange[] levels)
    {
        LastUpdateId = lastUpdateId;
        Levels = levels;
    }

    public long LastUpdateId { get; }

    public LevelChange[] Levels { get; }
}

/// <summary>Renders a canonical <see cref="InstrumentId"/> into Binance wire formats.</summary>
public static class BinanceSymbols
{
    /// <summary>REST/upper form, e.g. <c>BTCUSDT</c>.</summary>
    public static string ToRest(InstrumentId instrument) =>
        instrument.Base + instrument.Quote;

    /// <summary>Stream/lower form, e.g. <c>btcusdt</c>.</summary>
    public static string ToStream(InstrumentId instrument) =>
        (instrument.Base + instrument.Quote).ToLowerInvariant();
}

/// <summary>Converts Binance JSON DTOs into the normalized event/snapshot shapes.</summary>
internal static class BinanceParsing
{
    public static BinanceDepthEvent ToDepthEvent(BinanceDepthDiffDto dto)
    {
        var bids = dto.Bids ?? [];
        var asks = dto.Asks ?? [];
        var levels = new LevelChange[bids.Length + asks.Length];
        var next = FillSide(levels, 0, bids, Side.Bid);
        FillSide(levels, next, asks, Side.Ask);
        return new BinanceDepthEvent(dto.FirstUpdateId, dto.FinalUpdateId, levels);
    }

    public static BinanceSnapshot ToSnapshot(BinanceSnapshotDto dto)
    {
        var bids = dto.Bids ?? [];
        var asks = dto.Asks ?? [];
        var levels = new LevelChange[bids.Length + asks.Length];
        var next = FillSide(levels, 0, bids, Side.Bid);
        FillSide(levels, next, asks, Side.Ask);
        return new BinanceSnapshot(dto.LastUpdateId, levels);
    }

    private static int FillSide(LevelChange[] target, int offset, string[][] rows, Side side)
    {
        foreach (var row in rows)
        {
            var price = decimal.Parse(row[0], CultureInfo.InvariantCulture);
            var quantity = decimal.Parse(row[1], CultureInfo.InvariantCulture);
            target[offset++] = new LevelChange(side, price, quantity);
        }

        return offset;
    }
}
