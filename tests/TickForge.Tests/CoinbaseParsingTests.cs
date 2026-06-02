using System.Text;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Coinbase;
using Xunit;

namespace TickForge.Tests;

public sealed class CoinbaseParsingTests
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void ParsesSnapshot_MappingBidAndOfferSides()
    {
        var json = """
            {"channel":"l2_data","sequence_num":7,"events":[
              {"type":"snapshot","product_id":"BTC-USDT","updates":[
                {"side":"bid","price_level":"100.5","new_quantity":"2"},
                {"side":"offer","price_level":"101.5","new_quantity":"3"}
              ]}]}
            """;

        var parsed = CoinbaseParsing.TryParse(Utf8(json), out var message);

        Assert.True(parsed);
        Assert.Equal(CoinbaseMessageKind.Snapshot, message.Kind);
        Assert.Equal(7, message.SequenceNum);
        Assert.Equal(2, message.Levels.Length);
        Assert.Equal(new LevelChange(Side.Bid, 100.5m, 2m), message.Levels[0]);
        Assert.Equal(new LevelChange(Side.Ask, 101.5m, 3m), message.Levels[1]);
    }

    [Fact]
    public void ParsesUpdate_WithZeroQuantityAsRemoval()
    {
        var json = """
            {"channel":"l2_data","sequence_num":8,"events":[
              {"type":"update","product_id":"BTC-USDT","updates":[
                {"side":"bid","price_level":"100.5","new_quantity":"0"}
              ]}]}
            """;

        var parsed = CoinbaseParsing.TryParse(Utf8(json), out var message);

        Assert.True(parsed);
        Assert.Equal(CoinbaseMessageKind.Update, message.Kind);
        Assert.Single(message.Levels);
        Assert.True(message.Levels[0].IsRemoval);
    }

    [Fact]
    public void NonLevel2Messages_AreKeptAsOther_ForSequencing()
    {
        // The subscriptions ack carries a sequence_num that must be counted, so
        // it parses as Other (not dropped) with no level changes.
        var json = """
            {"channel":"subscriptions","sequence_num":4,"events":[
              {"subscriptions":{"level2":["BTC-USDT"]}}]}
            """;

        var parsed = CoinbaseParsing.TryParse(Utf8(json), out var message);

        Assert.True(parsed);
        Assert.Equal(CoinbaseMessageKind.Other, message.Kind);
        Assert.Equal(4, message.SequenceNum);
        Assert.Empty(message.Levels);
    }

    [Fact]
    public void RendersCanonicalProductId()
    {
        Assert.Equal("BTC-USDT", CoinbaseSymbols.ToProductId(InstrumentId.Parse("BTC/USDT")));
    }
}
