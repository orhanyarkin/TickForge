using System;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using TickForge.Core.Time;
using TickForge.Latency;

namespace TickForge.Web;

/// <summary>
/// One exchange's live state for the dashboard. It is the <see cref="IBookSink"/>
/// the adapter pushes into and the source the broadcaster reads from, so book
/// writes (apply) and reads (snapshot for the UI) are serialized under one lock —
/// the order book is not thread-safe and the UI reads it continuously.
///
/// The latency histogram is concurrent, so latency recording and snapshots stay
/// outside the lock.
/// </summary>
internal sealed class ExchangeFeed : IBookSink
{
    private readonly object _gate = new();
    private readonly OrderBook _book = new();
    private readonly LatencyRecorder _latency;
    private readonly Action<string>? _log;

    public ExchangeFeed(string name, IFeedAdapter adapter, long expectedIntervalNanos, Action<string>? log = null)
    {
        Name = name;
        Adapter = adapter;
        _latency = new LatencyRecorder(expectedIntervalNanos);
        _log = log;
    }

    public string Name { get; }
    public IFeedAdapter Adapter { get; }

    public void OnSnapshot(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        lock (_gate)
            _book.ApplySnapshot(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnDelta(InstrumentId instrument, ReadOnlySpan<LevelChange> levels, long recvTsNanos)
    {
        lock (_gate)
            _book.ApplyDelta(levels);
        _latency.Record(Clock.NowNanos() - recvTsNanos);
    }

    public void OnResync(InstrumentId instrument, string reason) =>
        _log?.Invoke($"[resync] {Name} {instrument}: {reason}");

    /// <summary>Build a consistent snapshot of this exchange's state for the UI.</summary>
    public ExchangeState BuildState(int depth)
    {
        Span<LevelChange> bidBuffer = stackalloc LevelChange[depth];
        Span<LevelChange> askBuffer = stackalloc LevelChange[depth];

        long version;
        decimal? bestBid, bestAsk, spread, mid;
        int bidCount, askCount;

        lock (_gate)
        {
            version = _book.Version;
            bestBid = _book.BestBid;
            bestAsk = _book.BestAsk;
            spread = _book.Spread;
            mid = _book.Mid;
            bidCount = _book.CopyTopLevels(Side.Bid, bidBuffer);
            askCount = _book.CopyTopLevels(Side.Ask, askBuffer);
        }

        var snapshot = _latency.Snapshot();
        return new ExchangeState(
            Name,
            version,
            bestBid is not null && bestAsk is not null,
            bestBid, bestAsk, spread, mid,
            ToLevels(bidBuffer[..bidCount]),
            ToLevels(askBuffer[..askCount]),
            ToLatencyView(snapshot));
    }

    private static Level[] ToLevels(ReadOnlySpan<LevelChange> source)
    {
        var levels = new Level[source.Length];
        for (var i = 0; i < source.Length; i++)
            levels[i] = new Level(source[i].Price, source[i].Quantity);
        return levels;
    }

    private static LatencyView ToLatencyView(LatencySnapshot s) => new(
        s.P50 / 1_000.0,
        s.P90 / 1_000.0,
        s.P99 / 1_000.0,
        s.P999 / 1_000.0,
        s.Max / 1_000.0,
        s.Count);
}
