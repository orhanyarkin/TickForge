using System.Text;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Coinbase;
using Xunit;

namespace TickForge.Tests;

public sealed class CoinbaseDepthParserTests
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private const string SnapshotFrame =
        """
        {"channel":"l2_data","client_id":"","timestamp":"2026-01-01T00:00:00Z","sequence_num":7,
         "events":[{"type":"snapshot","product_id":"BTC-USDT","updates":[
           {"side":"bid","event_time":"2026-01-01T00:00:00Z","price_level":"100.5","new_quantity":"2"},
           {"side":"offer","event_time":"2026-01-01T00:00:00Z","price_level":"101.5","new_quantity":"3"},
           {"side":"bid","event_time":"2026-01-01T00:00:00Z","price_level":"100.0","new_quantity":"0"}
         ]}]}
        """;

    [Fact]
    public void HandRolled_MatchesSourceGen()
    {
        var bytes = Utf8(SnapshotFrame);
        Assert.True(CoinbaseParsing.TryParse(bytes, out var expected));

        var buffer = new LevelChange[64];
        var ok = CoinbaseDepthParser.TryParse(bytes, buffer, out var seq, out var kind, out var count);

        Assert.True(ok);
        Assert.Equal(expected.SequenceNum, seq);
        Assert.Equal(expected.Kind, kind);
        Assert.Equal(expected.Levels.Length, count);
        for (var i = 0; i < count; i++)
            Assert.Equal(expected.Levels[i], buffer[i]);
    }

    [Fact]
    public void MapsBidAndOfferSides_AndZeroQuantityRemoval()
    {
        var buffer = new LevelChange[64];

        var ok = CoinbaseDepthParser.TryParse(Utf8(SnapshotFrame), buffer, out var seq, out var kind, out var count);

        Assert.True(ok);
        Assert.Equal(7, seq);
        Assert.Equal(CoinbaseMessageKind.Snapshot, kind);
        Assert.Equal(3, count);
        Assert.Equal(new LevelChange(Side.Bid, 100.5m, 2m), buffer[0]);
        Assert.Equal(new LevelChange(Side.Ask, 101.5m, 3m), buffer[1]);
        Assert.True(buffer[2].IsRemoval);
    }

    [Fact]
    public void Update_ParsesAsUpdateKind()
    {
        var json =
            """{"channel":"l2_data","sequence_num":8,"events":[{"type":"update","product_id":"BTC-USDT","updates":[{"side":"bid","price_level":"100.5","new_quantity":"1.5"}]}]}""";
        var buffer = new LevelChange[8];

        var ok = CoinbaseDepthParser.TryParse(Utf8(json), buffer, out var seq, out var kind, out var count);

        Assert.True(ok);
        Assert.Equal(8, seq);
        Assert.Equal(CoinbaseMessageKind.Update, kind);
        Assert.Single(buffer[..count]);
        Assert.Equal(new LevelChange(Side.Bid, 100.5m, 1.5m), buffer[0]);
    }

    [Fact]
    public void NonLevel2Message_ParsesAsOther_KeepingSequence()
    {
        var json =
            """{"channel":"subscriptions","sequence_num":4,"events":[{"subscriptions":{"level2":["BTC-USDT"]}}]}""";
        var buffer = new LevelChange[8];

        var ok = CoinbaseDepthParser.TryParse(Utf8(json), buffer, out var seq, out var kind, out var count);

        Assert.True(ok);
        Assert.Equal(4, seq);
        Assert.Equal(CoinbaseMessageKind.Other, kind);
        Assert.Equal(0, count);
    }
}
