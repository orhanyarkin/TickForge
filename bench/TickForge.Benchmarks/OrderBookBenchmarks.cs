using BenchmarkDotNet.Attributes;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;

namespace TickForge.Benchmarks;

/// <summary>
/// Measures <see cref="OrderBook.ApplyDelta"/> applying a batch of top-of-book
/// changes to a book pre-seeded with a deep snapshot.
/// </summary>
[MemoryDiagnoser]
public class OrderBookBenchmarks
{
    private const int DepthPerSide = 1_000;

    private OrderBook _book = null!;
    private LevelChange[] _delta = null!;

    [GlobalSetup]
    public void Setup()
    {
        _book = new OrderBook();

        var snapshot = new LevelChange[DepthPerSide * 2];
        var k = 0;
        for (var i = 0; i < DepthPerSide; i++)
            snapshot[k++] = new LevelChange(Side.Bid, 60_000m - (i * 0.01m), 1m + (i % 5 * 0.1m));
        for (var i = 0; i < DepthPerSide; i++)
            snapshot[k++] = new LevelChange(Side.Ask, 60_010m + (i * 0.01m), 1m + (i % 5 * 0.1m));
        _book.ApplySnapshot(snapshot);

        // A 64-level delta touching levels near the top of book.
        _delta = new LevelChange[64];
        for (var i = 0; i < 32; i++)
            _delta[i] = new LevelChange(Side.Bid, 60_000m - (i * 0.01m), 2m + (i % 3));
        for (var i = 0; i < 32; i++)
            _delta[32 + i] = new LevelChange(Side.Ask, 60_010m + (i * 0.01m), 2m + (i % 3));
    }

    [Benchmark]
    public long ApplyDelta()
    {
        _book.ApplyDelta(_delta);
        return _book.Version;
    }
}
