using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using TickForge.Feeds.Binance;
using TickForge.Feeds.Coinbase;
using TickForge.Latency;

var options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 1;
}

var instrument = InstrumentId.Parse(options.Symbol);

// One independent book + latency recorder per exchange so the per-exchange
// breakdown is a true apples-to-apples comparison.
var feeds = new List<ExchangeFeed>();
foreach (var exchange in options.Exchanges)
{
    IFeedAdapter adapter = exchange switch
    {
        "binance" => new BinanceFeedAdapter(log: Console.Error.WriteLine),
        "coinbase" => new CoinbaseFeedAdapter(log: Console.Error.WriteLine),
        _ => throw new ArgumentException($"Unknown exchange '{exchange}'. Supported: binance, coinbase."),
    };

    var book = new OrderBook();
    // ~1 ms expected steady-state cadence for coordinated-omission correction.
    var latency = new LatencyRecorder(expectedIntervalNanos: 1_000_000);
    var sink = new LatencyBookSink(book, latency, Console.Error.WriteLine);
    feeds.Add(new ExchangeFeed(adapter.ExchangeName, adapter, sink));
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"TickForge — {instrument} on {string.Join(", ", options.Exchanges)} (Ctrl+C to stop)"));

var feedTasks = new Task[feeds.Count];
for (var i = 0; i < feeds.Count; i++)
    feedTasks[i] = feeds[i].Adapter.RunAsync([instrument], feeds[i].Sink, cts.Token);

try
{
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
        foreach (var feed in feeds)
            PrintFeed(feed);
        Console.WriteLine();
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — fall through to a clean shutdown.
}

await Task.WhenAll(feedTasks).ConfigureAwait(false);
foreach (var feed in feeds)
    await feed.Adapter.DisposeAsync().ConfigureAwait(false);
return 0;

static void PrintFeed(ExchangeFeed feed)
{
    var top = feed.Sink.ReadTop();
    var latency = feed.Sink.Snapshot();
    string line = top.BestBid is { } b && top.BestAsk is { } a
        ? string.Format(
            CultureInfo.InvariantCulture,
            "bid {0} | ask {1} | spread {2} | mid {3}",
            b, a, top.Spread, top.Mid)
        : "book warming up…";

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"{feed.Label,-9} v{top.Version,-8} {line,-64} {latency}"));
}

/// <summary>One exchange's wiring: its adapter and its synchronized book sink.</summary>
internal sealed class ExchangeFeed(string label, IFeedAdapter adapter, LatencyBookSink sink)
{
    public string Label { get; } = label;
    public IFeedAdapter Adapter { get; } = adapter;
    public LatencyBookSink Sink { get; } = sink;
}

/// <summary>Parsed command-line options for the console host.</summary>
internal sealed class CliOptions
{
    private static readonly string[] AllExchanges = ["binance", "coinbase"];

    public required string Symbol { get; init; }
    public required IReadOnlyList<string> Exchanges { get; init; }

    public static CliOptions? Parse(string[] args)
    {
        var symbol = "BTC/USDT";
        var exchanges = "all";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--symbol" when i + 1 < args.Length:
                    symbol = args[++i];
                    break;
                case "--exchange" when i + 1 < args.Length:
                    exchanges = args[++i].ToLowerInvariant();
                    break;
                case "-h" or "--help":
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    return null;
            }
        }

        var selected = exchanges == "all"
            ? AllExchanges
            : exchanges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new CliOptions { Symbol = symbol, Exchanges = selected };
    }

    public static void PrintUsage() =>
        Console.WriteLine(
            "Usage: TickForge.App [--exchange all|binance|coinbase[,…]] [--symbol BTC/USDT]");
}
