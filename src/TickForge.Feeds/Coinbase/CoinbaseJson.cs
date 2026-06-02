using System.Text.Json.Serialization;

namespace TickForge.Feeds.Coinbase;

/// <summary>
/// Envelope for a Coinbase Advanced Trade <c>level2</c> message. The
/// <c>sequence_num</c> increments per message on the connection; with only the
/// <c>level2</c> channel subscribed, consecutive <c>l2_data</c> messages are
/// contiguous, so a non-contiguous value means a gap.
/// </summary>
internal sealed class CoinbaseL2Envelope
{
    [JsonPropertyName("channel")] public string? Channel { get; set; }
    [JsonPropertyName("sequence_num")] public long SequenceNum { get; set; }
    [JsonPropertyName("events")] public CoinbaseL2Event[]? Events { get; set; }
}

internal sealed class CoinbaseL2Event
{
    /// <summary><c>snapshot</c> for the initial book, <c>update</c> afterwards.</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("product_id")] public string? ProductId { get; set; }

    [JsonPropertyName("updates")] public CoinbaseL2Update[]? Updates { get; set; }
}

internal sealed class CoinbaseL2Update
{
    /// <summary><c>bid</c> or <c>offer</c>.</summary>
    [JsonPropertyName("side")] public string? Side { get; set; }

    [JsonPropertyName("price_level")] public string? PriceLevel { get; set; }

    [JsonPropertyName("new_quantity")] public string? NewQuantity { get; set; }
}

/// <summary>Source-generated JSON context for the Coinbase level2 envelope.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
[JsonSerializable(typeof(CoinbaseL2Envelope))]
internal sealed partial class CoinbaseJsonContext : JsonSerializerContext
{
}
