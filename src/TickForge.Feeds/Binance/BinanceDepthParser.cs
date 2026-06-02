using System;
using System.Buffers.Text;
using System.Text.Json;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Binance;

/// <summary>
/// Allocation-free, hand-rolled <see cref="Utf8JsonReader"/> parser for Binance
/// <c>depthUpdate</c> frames. It writes <see cref="LevelChange"/> values straight
/// into a caller-supplied buffer, skipping the intermediate <c>string[][]</c>
/// arrays (and the <c>string</c> per price/quantity) that the source-generated
/// DTO path allocates.
///
/// This is the "fast path" benchmarked against the source-gen path in
/// <c>TickForge.Benchmarks</c>; correctness parity is asserted in the tests.
/// </summary>
public static class BinanceDepthParser
{
    /// <summary>
    /// Parse a depth frame into <paramref name="destination"/>. Returns false if
    /// the buffer is too small or the frame is malformed.
    /// </summary>
    public static bool TryParse(
        ReadOnlySpan<byte> json,
        Span<LevelChange> destination,
        out int count,
        out long firstUpdateId,
        out long finalUpdateId)
    {
        count = 0;
        firstUpdateId = 0;
        finalUpdateId = 0;

        var reader = new Utf8JsonReader(json);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("U"u8))
            {
                reader.Read();
                firstUpdateId = reader.GetInt64();
            }
            else if (reader.ValueTextEquals("u"u8))
            {
                reader.Read();
                finalUpdateId = reader.GetInt64();
            }
            else if (reader.ValueTextEquals("b"u8))
            {
                if (!ReadLevels(ref reader, Side.Bid, destination, ref count))
                    return false;
            }
            else if (reader.ValueTextEquals("a"u8))
            {
                if (!ReadLevels(ref reader, Side.Ask, destination, ref count))
                    return false;
            }
        }

        return true;
    }

    private static bool ReadLevels(
        ref Utf8JsonReader reader, Side side, Span<LevelChange> destination, ref int count)
    {
        reader.Read(); // value of the property -> the outer array
        if (reader.TokenType != JsonTokenType.StartArray)
            return false;

        // Each element is a ["price","qty"] pair; the loop ends on the outer EndArray.
        while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            if (!TryReadDecimalString(ref reader, out var price))
                return false;

            reader.Read();
            if (!TryReadDecimalString(ref reader, out var quantity))
                return false;

            reader.Read(); // EndArray of the pair

            if (count >= destination.Length)
                return false;

            destination[count++] = new LevelChange(side, price, quantity);
        }

        return true;
    }

    private static bool TryReadDecimalString(ref Utf8JsonReader reader, out decimal value)
    {
        // Binance encodes price/quantity as JSON strings of plain decimals (no
        // escaping), so the raw value bytes parse directly.
        if (reader.TokenType != JsonTokenType.String)
        {
            value = 0m;
            return false;
        }

        return Utf8Parser.TryParse(reader.ValueSpan, out value, out _);
    }
}
