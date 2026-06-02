using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TickForge.Web;

/// <summary>The full dashboard payload broadcast to browser clients.</summary>
internal sealed record DashboardState(
    string Symbol,
    long Timestamp,
    IReadOnlyList<ExchangeState> Exchanges);

/// <summary>One exchange's view: top-of-book, the visible ladder, and latency.</summary>
internal sealed record ExchangeState(
    string Name,
    long Version,
    bool Ready,
    decimal? BestBid,
    decimal? BestAsk,
    decimal? Spread,
    decimal? Mid,
    IReadOnlyList<Level> Bids,
    IReadOnlyList<Level> Asks,
    LatencyView Latency);

/// <summary>A single ladder level.</summary>
internal sealed record Level(decimal Price, decimal Quantity);

/// <summary>Latency percentiles in microseconds.</summary>
internal sealed record LatencyView(
    double P50,
    double P90,
    double P99,
    double P999,
    double Max,
    long Count);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DashboardState))]
internal sealed partial class DashboardJsonContext : JsonSerializerContext
{
}
