using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;
using TickForge.Feeds.Common;

namespace TickForge.Feeds.Coinbase;

/// <summary>
/// Coinbase L2 feed adapter over the Advanced Trade WebSocket
/// (<c>wss://advanced-trade-ws.coinbase.com</c>, <c>level2</c> channel — public,
/// no auth in the current beta). It subscribes on connect, then drives a
/// <see cref="CoinbaseReconciler"/> for snapshot-first bootstrap and
/// <c>sequence_num</c> gap detection. A gap triggers a reconnect, which
/// re-subscribes and yields a fresh snapshot.
///
/// All work is driven synchronously by the single read-loop thread, so no
/// locking is needed.
/// </summary>
public sealed class CoinbaseFeedAdapter : WebSocketFeedBase, IFeedAdapter
{
    private static readonly Uri Endpoint = new("wss://advanced-trade-ws.coinbase.com");

    private readonly Action<string>? _log;

    private CoinbaseReconciler? _reconciler;
    private InstrumentId _instrument;

    // Reused per-frame parse buffer; an overflow skips the frame and the next
    // sequence check resyncs.
    private readonly LevelChange[] _parseBuffer = new LevelChange[4096];

    public CoinbaseFeedAdapter(Action<string>? log = null) => _log = log;

    public string ExchangeName => "Coinbase";

    public Task RunAsync(IReadOnlyList<InstrumentId> instruments, IBookSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(sink);
        if (instruments.Count == 0)
            throw new ArgumentException("At least one instrument is required.", nameof(instruments));

        _instrument = instruments[0];
        _reconciler = new CoinbaseReconciler(_instrument, sink, RequestReconnect);

        return RunLoopAsync([_instrument], ct);
    }

    protected override Uri BuildEndpoint(IReadOnlyList<InstrumentId> instruments) => Endpoint;

    protected override async Task OnConnectedAsync(
        ClientWebSocket socket, IReadOnlyList<InstrumentId> instruments, CancellationToken ct)
    {
        var productId = CoinbaseSymbols.ToProductId(instruments[0]);
        var subscribe = $"{{\"type\":\"subscribe\",\"product_ids\":[\"{productId}\"],\"channel\":\"level2\"}}";
        var bytes = Encoding.UTF8.GetBytes(subscribe);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct)
            .ConfigureAwait(false);
    }

    protected override void OnConnected(IReadOnlyList<InstrumentId> instruments) =>
        _reconciler!.Reset();

    protected override void OnMessage(ReadOnlySpan<byte> payload, long recvTsNanos)
    {
        // Allocation-free parse straight into the reused buffer; the span flows
        // through to the book apply without a copy.
        if (CoinbaseDepthParser.TryParse(payload, _parseBuffer, out var seq, out var kind, out var count))
            _reconciler!.OnMessage(seq, kind, _parseBuffer.AsSpan(0, count), recvTsNanos);
    }

    protected override void OnError(Exception exception) =>
        _log?.Invoke($"[Coinbase] {exception.GetType().Name}: {exception.Message}");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
