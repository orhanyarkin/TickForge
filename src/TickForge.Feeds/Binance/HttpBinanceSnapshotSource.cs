using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;

namespace TickForge.Feeds.Binance;

/// <summary>
/// Fetches the Binance depth snapshot from <c>/api/v3/depth</c> over HTTPS.
/// The endpoint is public and needs no API key.
/// </summary>
public sealed class HttpBinanceSnapshotSource : IBinanceSnapshotSource, IDisposable
{
    private const string DefaultBaseUrl = "https://api.binance.com";
    private const int SnapshotDepth = 1000;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _baseUrl;

    public HttpBinanceSnapshotSource(HttpClient? http = null, string baseUrl = DefaultBaseUrl)
    {
        _http = http ?? new HttpClient();
        _ownsClient = http is null;
        _baseUrl = baseUrl;
    }

    public async Task<BinanceSnapshot> FetchAsync(InstrumentId instrument, CancellationToken ct)
    {
        var symbol = BinanceSymbols.ToRest(instrument);
        var url = string.Format(
            CultureInfo.InvariantCulture,
            "{0}/api/v3/depth?symbol={1}&limit={2}",
            _baseUrl, symbol, SnapshotDepth);

        await using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
        var dto = await JsonSerializer
            .DeserializeAsync(stream, BinanceJsonContext.Default.BinanceSnapshotDto, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty Binance snapshot response.");

        return BinanceParsing.ToSnapshot(dto);
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
