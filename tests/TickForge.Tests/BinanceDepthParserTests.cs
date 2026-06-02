using System.Text;
using System.Text.Json;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Binance;
using Xunit;

namespace TickForge.Tests;

public sealed class BinanceDepthParserTests
{
    private const string SampleFrame =
        """
        {"e":"depthUpdate","E":1700000000123,"s":"BTCUSDT","U":160000100,"u":160000142,
        "b":[["67178.44000000","1.23450000"],["67178.43000000","0.50000000"],
        ["67178.40000000","2.00000000"],["67177.99000000","3.33000000"]],
        "a":[["67178.45000000","0.80000000"],["67178.46000000","1.10000000"],
        ["67178.50000000","3.30000000"],["67179.10000000","2.22000000"]]}
        """;

    private static byte[] Frame() => Encoding.UTF8.GetBytes(SampleFrame);

    [Fact]
    public void HandRolled_MatchesSourceGen()
    {
        var bytes = Frame();
        var dto = JsonSerializer.Deserialize(bytes, BinanceJsonContext.Default.BinanceDepthDiffDto)!;
        var expected = BinanceParsing.ToDepthEvent(dto);

        var buffer = new LevelChange[256];
        var ok = BinanceDepthParser.TryParse(
            bytes, buffer, out var count, out var firstId, out var finalId);

        Assert.True(ok);
        Assert.Equal(expected.FirstUpdateId, firstId);
        Assert.Equal(expected.FinalUpdateId, finalId);
        Assert.Equal(expected.Levels.Length, count);
        for (var i = 0; i < count; i++)
            Assert.Equal(expected.Levels[i], buffer[i]);
    }

    [Fact]
    public void ParsesIds_SidesAndZeroQuantityRemoval()
    {
        var json = Encoding.UTF8.GetBytes(
            """{"U":5,"u":9,"b":[["100.5","0"]],"a":[["101.5","2"]]}""");
        var buffer = new LevelChange[8];

        var ok = BinanceDepthParser.TryParse(
            json, buffer, out var count, out var firstId, out var finalId);

        Assert.True(ok);
        Assert.Equal(5, firstId);
        Assert.Equal(9, finalId);
        Assert.Equal(2, count);
        Assert.Equal(new LevelChange(Side.Bid, 100.5m, 0m), buffer[0]);
        Assert.True(buffer[0].IsRemoval);
        Assert.Equal(new LevelChange(Side.Ask, 101.5m, 2m), buffer[1]);
    }

    [Fact]
    public void TryParse_ReturnsFalse_WhenBufferTooSmall()
    {
        var tiny = new LevelChange[2];

        var ok = BinanceDepthParser.TryParse(Frame(), tiny, out _, out _, out _);

        Assert.False(ok);
    }
}
