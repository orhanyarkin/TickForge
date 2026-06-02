using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TickForge.App;
using TickForge.Core.Abstractions;
using TickForge.Core.Book;
using TickForge.Core.Time;
using TickForge.Feeds.Binance;
using TickForge.Latency;

var options = CliOptions.Parse(args);
if (options is null)
{
    CliOptions.PrintUsage();
    return 1;
}

var instrument = InstrumentId.Parse(options.Symbol);

var book = new OrderBook();
// Expected steady-state interval between samples (~1 ms) for coordinated-omission
// correction; the diff stream is bursty, so this is a deliberate approximation.
var latency = new LatencyRecorder(expectedIntervalNanos: 1_000_000);
var sink = new LatencyBookSink(book, latency, Console.Error.WriteLine);

IFeedAdapter adapter = options.Exchange switch
{
    "binance" => new BinanceFeedAdapter(log: Console.Error.WriteLine),
    _ => throw new ArgumentException($"Unknown exchange '{options.Exchange}'. Phase 1 supports 'binance'."),
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"TickForge — {adapter.ExchangeName} {instrument} (Ctrl+C to stop)"));

var feedTask = adapter.RunAsync([instrument], sink, cts.Token);

try
{
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token).ConfigureAwait(false);
        PrintStatus(book, latency.Snapshot());
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — fall through to a clean shutdown.
}

await feedTask.ConfigureAwait(false);
await adapter.DisposeAsync().ConfigureAwait(false);
return 0;

static void PrintStatus(OrderBook book, LatencySnapshot latency)
{
    var bid = book.BestBid;
    var ask = book.BestAsk;
    string top = bid is { } b && ask is { } a
        ? string.Format(
            CultureInfo.InvariantCulture,
            "bid {0} | ask {1} | spread {2} | mid {3}",
            b, a, book.Spread, book.Mid)
        : "book warming up…";

    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"v{book.Version,-8} {top,-64} {latency}"));
}

/// <summary>Parsed command-line options for the console host.</summary>
internal sealed class CliOptions
{
    public required string Symbol { get; init; }
    public required string Exchange { get; init; }

    public static CliOptions? Parse(string[] args)
    {
        var symbol = "BTC/USDT";
        var exchange = "binance";

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--symbol" when i + 1 < args.Length:
                    symbol = args[++i];
                    break;
                case "--exchange" when i + 1 < args.Length:
                    exchange = args[++i].ToLowerInvariant();
                    break;
                case "-h" or "--help":
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    return null;
            }
        }

        return new CliOptions { Symbol = symbol, Exchange = exchange };
    }

    public static void PrintUsage() =>
        Console.WriteLine(
            "Usage: TickForge.App [--exchange binance] [--symbol BTC/USDT]");
}
