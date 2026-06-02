using BenchmarkDotNet.Attributes;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;

namespace TickForge.Benchmarks;

/// <summary>
/// The decimal-vs-scaled-long trade-off: applying the same batch of price-level
/// updates to the production <see cref="BookSide"/> (<see langword="decimal"/>)
/// versus the experimental <see cref="ScaledBookSide"/> (<see langword="long"/>
/// ticks). Both are pre-seeded with the same levels.
/// </summary>
[MemoryDiagnoser]
public class BookSideBenchmarks
{
    private const int Levels = 1_000;
    private const decimal PriceScale = 100m;          // 2-dp price ticks
    private const decimal QuantityScale = 100_000_000m; // 8-dp quantity ticks

    private LevelChange[] _decimalUpdates = null!;
    private (long Price, long Quantity)[] _tickUpdates = null!;
    private BookSide _decimalSide = null!;
    private ScaledBookSide _scaledSide = null!;

    [GlobalSetup]
    public void Setup()
    {
        _decimalSide = new BookSide(Side.Bid);
        _scaledSide = new ScaledBookSide(Side.Bid);
        _decimalUpdates = new LevelChange[Levels];
        _tickUpdates = new (long, long)[Levels];

        for (var i = 0; i < Levels; i++)
        {
            var price = 60_000m + (i * 0.01m);
            var quantity = 1m + (i % 10 * 0.1m);
            _decimalUpdates[i] = new LevelChange(Side.Bid, price, quantity);
            _tickUpdates[i] = ((long)(price * PriceScale), (long)(quantity * QuantityScale));

            // Pre-seed both sides so the benchmark measures updates, not inserts.
            _decimalSide.Apply(price, quantity);
            _scaledSide.Apply(_tickUpdates[i].Item1, _tickUpdates[i].Item2);
        }
    }

    [Benchmark(Baseline = true)]
    public int DecimalSide()
    {
        var side = _decimalSide;
        var updates = _decimalUpdates;
        for (var i = 0; i < updates.Length; i++)
            side.Apply(updates[i].Price, updates[i].Quantity);
        return side.Count;
    }

    [Benchmark]
    public int ScaledLongSide()
    {
        var side = _scaledSide;
        var updates = _tickUpdates;
        for (var i = 0; i < updates.Length; i++)
            side.Apply(updates[i].Price, updates[i].Quantity);
        return side.Count;
    }
}
