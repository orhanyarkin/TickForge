using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Binance;
using TickForge.Feeds.Coinbase;

namespace TickForge.Web;

/// <summary>
/// Hosts the feed adapters for the lifetime of the web app: it builds one
/// <see cref="ExchangeFeed"/> per exchange, runs each adapter on a background
/// task, and serves a consistent JSON snapshot of all books + latency for the
/// dashboard. Symbol and exchanges come from configuration
/// (<c>TICKFORGE_SYMBOL</c>, <c>TICKFORGE_EXCHANGES</c>).
/// </summary>
public sealed class MarketDataService : IHostedService, IDisposable
{
    private const int LadderDepth = 12;
    private const long ExpectedIntervalNanos = 1_000_000; // ~1 ms steady-state cadence

    private readonly ILogger<MarketDataService> _logger;
    private readonly InstrumentId _instrument;
    private readonly List<ExchangeFeed> _feeds = [];
    private readonly CancellationTokenSource _cts = new();

    private Task[] _tasks = [];

    public MarketDataService(IConfiguration configuration, ILogger<MarketDataService> logger)
    {
        _logger = logger;
        Symbol = configuration["TICKFORGE_SYMBOL"] ?? "BTC/USDT";
        _instrument = InstrumentId.Parse(Symbol);

        var exchanges = (configuration["TICKFORGE_EXCHANGES"] ?? "binance,coinbase")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var exchange in exchanges)
            _feeds.Add(CreateFeed(exchange.ToLowerInvariant()));
    }

    public string Symbol { get; }

    private ExchangeFeed CreateFeed(string exchange)
    {
        IFeedAdapter adapter = exchange switch
        {
            "binance" => new BinanceFeedAdapter(log: Log),
            "coinbase" => new CoinbaseFeedAdapter(log: Log),
            _ => throw new ArgumentException($"Unknown exchange '{exchange}'. Supported: binance, coinbase."),
        };

        return new ExchangeFeed(adapter.ExchangeName, adapter, ExpectedIntervalNanos, Log);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _tasks = new Task[_feeds.Count];
        for (var i = 0; i < _feeds.Count; i++)
            _tasks[i] = _feeds[i].Adapter.RunAsync([_instrument], _feeds[i], _cts.Token);

        _logger.LogInformation("TickForge feeds started for {Symbol} on {Count} exchange(s).",
            Symbol, _feeds.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        try
        {
            await Task.WhenAll(_tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        foreach (var feed in _feeds)
            await feed.Adapter.DisposeAsync();
    }

    /// <summary>Snapshot the whole dashboard state.</summary>
    internal DashboardState BuildState()
    {
        var exchanges = new ExchangeState[_feeds.Count];
        for (var i = 0; i < _feeds.Count; i++)
            exchanges[i] = _feeds[i].BuildState(LadderDepth);

        return new DashboardState(Symbol, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), exchanges);
    }

    /// <summary>Serialize the dashboard state to UTF-8 JSON for the wire.</summary>
    public byte[] BuildStateJson() =>
        JsonSerializer.SerializeToUtf8Bytes(BuildState(), DashboardJsonContext.Default.DashboardState);

    private void Log(string message) => _logger.LogInformation("{Message}", message);

    public void Dispose() => _cts.Dispose();
}
