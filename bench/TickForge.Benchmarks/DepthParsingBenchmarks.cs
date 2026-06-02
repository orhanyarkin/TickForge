using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Binance;

namespace TickForge.Benchmarks;

/// <summary>
/// Binance depth-frame parsing: the source-generated DTO path (which allocates
/// <c>string[][]</c> plus a string per price/quantity) versus the hand-rolled
/// <see cref="BinanceDepthParser"/> that writes straight into a reused buffer.
/// </summary>
[MemoryDiagnoser]
public class DepthParsingBenchmarks
{
    private const string SampleFrame =
        """
        {"e":"depthUpdate","E":1700000000123,"s":"BTCUSDT","U":160000100,"u":160000142,
        "b":[["67178.44000000","1.23450000"],["67178.43000000","0.50000000"],
        ["67178.40000000","2.00000000"],["67178.10000000","0.10000000"],
        ["67177.99000000","3.33000000"],["67177.50000000","0.75000000"],
        ["67177.00000000","1.00000000"],["67176.20000000","0.25000000"]],
        "a":[["67178.45000000","0.80000000"],["67178.46000000","1.10000000"],
        ["67178.50000000","3.30000000"],["67178.90000000","0.05000000"],
        ["67179.10000000","2.22000000"],["67179.75000000","0.90000000"],
        ["67180.00000000","1.50000000"],["67181.30000000","0.40000000"]]}
        """;

    private static readonly byte[] Frame = Encoding.UTF8.GetBytes(SampleFrame);

    private readonly LevelChange[] _buffer = new LevelChange[512];

    [Benchmark(Baseline = true)]
    public long SourceGen()
    {
        var dto = JsonSerializer.Deserialize(Frame, BinanceJsonContext.Default.BinanceDepthDiffDto)!;
        var evt = BinanceParsing.ToDepthEvent(dto);
        return evt.FinalUpdateId + evt.Levels.Length;
    }

    [Benchmark]
    public long HandRolled()
    {
        BinanceDepthParser.TryParse(Frame, _buffer, out var count, out _, out var finalUpdateId);
        return finalUpdateId + count;
    }
}
