using System.Text.Json.Serialization;

namespace TickForge.Feeds.Binance;

/// <summary>
/// DTO for a Binance <c>depthUpdate</c> diff event. Note <c>U</c> and <c>u</c>
/// differ only by case, so the source-gen context must stay case-sensitive
/// (the default) — these explicit names map them apart.
/// </summary>
internal sealed class BinanceDepthDiffDto
{
    [JsonPropertyName("U")] public long FirstUpdateId { get; set; }
    [JsonPropertyName("u")] public long FinalUpdateId { get; set; }
    [JsonPropertyName("b")] public string[][]? Bids { get; set; }
    [JsonPropertyName("a")] public string[][]? Asks { get; set; }
}

/// <summary>DTO for the REST <c>/api/v3/depth</c> snapshot response.</summary>
internal sealed class BinanceSnapshotDto
{
    [JsonPropertyName("lastUpdateId")] public long LastUpdateId { get; set; }
    [JsonPropertyName("bids")] public string[][]? Bids { get; set; }
    [JsonPropertyName("asks")] public string[][]? Asks { get; set; }
}

/// <summary>
/// Source-generated JSON context — the Phase-1 parsing path. A hand-rolled
/// allocation-free <c>Utf8JsonReader</c> parser is benchmarked against this in a
/// later phase.
/// </summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(BinanceDepthDiffDto))]
[JsonSerializable(typeof(BinanceSnapshotDto))]
internal sealed partial class BinanceJsonContext : JsonSerializerContext
{
}
