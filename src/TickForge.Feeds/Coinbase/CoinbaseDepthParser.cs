using System;
using System.Buffers.Text;
using System.Text.Json;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Coinbase;

/// <summary>
/// Allocation-free, hand-rolled <see cref="Utf8JsonReader"/> parser for Coinbase
/// level2 frames. It writes <see cref="LevelChange"/> values straight into a
/// caller-supplied buffer and reports the message's <c>sequence_num</c> and kind,
/// skipping the DTO/array allocations of the source-generated path.
///
/// Non-<c>l2_data</c> messages still carry a <c>sequence_num</c> that must be
/// counted for continuity, so they parse as <see cref="CoinbaseMessageKind.Other"/>
/// with no levels. Correctness parity with the source-gen path is asserted in tests.
/// </summary>
public static class CoinbaseDepthParser
{
    public static bool TryParse(
        ReadOnlySpan<byte> json,
        Span<LevelChange> destination,
        out long sequenceNum,
        out CoinbaseMessageKind kind,
        out int count)
    {
        sequenceNum = 0;
        kind = CoinbaseMessageKind.Other;
        count = 0;

        var reader = new Utf8JsonReader(json);
        var isLevel2 = false;
        var sawSnapshot = false;

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("channel"u8))
            {
                reader.Read();
                if (reader.ValueTextEquals("l2_data"u8))
                    isLevel2 = true;
            }
            else if (reader.ValueTextEquals("sequence_num"u8))
            {
                reader.Read();
                sequenceNum = reader.GetInt64();
            }
            else if (reader.ValueTextEquals("events"u8))
            {
                if (!ReadEvents(ref reader, destination, ref count, ref sawSnapshot))
                    return false;
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        if (!isLevel2)
        {
            // Non-book message (e.g. the subscriptions ack): keep only the sequence.
            kind = CoinbaseMessageKind.Other;
            count = 0;
            return true;
        }

        kind = sawSnapshot ? CoinbaseMessageKind.Snapshot : CoinbaseMessageKind.Update;
        return true;
    }

    private static bool ReadEvents(
        ref Utf8JsonReader reader, Span<LevelChange> destination, ref int count, ref bool sawSnapshot)
    {
        reader.Read(); // value of "events" -> array
        if (reader.TokenType != JsonTokenType.StartArray)
            return false;

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    if (reader.ValueTextEquals("snapshot"u8))
                        sawSnapshot = true;
                }
                else if (reader.ValueTextEquals("updates"u8))
                {
                    if (!ReadUpdates(ref reader, destination, ref count))
                        return false;
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }
        }

        return true;
    }

    private static bool ReadUpdates(ref Utf8JsonReader reader, Span<LevelChange> destination, ref int count)
    {
        reader.Read(); // value of "updates" -> array
        if (reader.TokenType != JsonTokenType.StartArray)
            return false;

        while (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            var side = Side.Ask;
            decimal price = 0m, quantity = 0m;
            bool havePrice = false, haveQuantity = false;

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("side"u8))
                {
                    reader.Read();
                    side = reader.ValueTextEquals("bid"u8) ? Side.Bid : Side.Ask;
                }
                else if (reader.ValueTextEquals("price_level"u8))
                {
                    reader.Read();
                    if (!Utf8Parser.TryParse(reader.ValueSpan, out price, out _))
                        return false;
                    havePrice = true;
                }
                else if (reader.ValueTextEquals("new_quantity"u8))
                {
                    reader.Read();
                    if (!Utf8Parser.TryParse(reader.ValueSpan, out quantity, out _))
                        return false;
                    haveQuantity = true;
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
            }

            if (havePrice && haveQuantity)
            {
                if (count >= destination.Length)
                    return false;
                destination[count++] = new LevelChange(side, price, quantity);
            }
        }

        return true;
    }
}
