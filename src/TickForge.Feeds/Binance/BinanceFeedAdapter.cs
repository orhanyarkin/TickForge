using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;
using TickForge.Core.Time;
using TickForge.Feeds.Common;

namespace TickForge.Feeds.Binance;

/// <summary>
/// Binance L2 feed adapter. Combines the diff stream
/// (<c>wss://stream.binance.com:9443/ws/&lt;sym&gt;@depth</c>) with the REST
/// snapshot and drives a <see cref="BinanceReconciler"/> for book consistency.
///
/// The reconciler is single-threaded by contract, so all calls into it are
/// serialized under <see cref="_gate"/>: the read loop (diff events) and the
/// async snapshot completion both take the lock before touching it.
/// </summary>
public sealed class BinanceFeedAdapter : WebSocketFeedBase, IFeedAdapter
{
    private const string StreamBaseUrl = "wss://stream.binance.com:9443/ws";

    private readonly IBinanceSnapshotSource _snapshotSource;
    private readonly bool _ownsSnapshotSource;
    private readonly Action<string>? _log;
    private readonly object _gate = new();

    // Reused per-frame parse buffer. Sized for the largest realistic diff; an
    // overflow simply skips the frame, and the next sequence check resyncs.
    private readonly LevelChange[] _parseBuffer = new LevelChange[4096];

    private IBookSink? _sink;
    private BinanceReconciler? _reconciler;
    private InstrumentId _instrument;
    private CancellationToken _ct;

    public BinanceFeedAdapter(IBinanceSnapshotSource? snapshotSource = null, Action<string>? log = null)
    {
        _snapshotSource = snapshotSource ?? new HttpBinanceSnapshotSource();
        _ownsSnapshotSource = snapshotSource is null;
        _log = log;
    }

    public string ExchangeName => "Binance";

    public Task RunAsync(IReadOnlyList<InstrumentId> instruments, IBookSink sink, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(sink);
        if (instruments.Count == 0)
            throw new ArgumentException("At least one instrument is required.", nameof(instruments));

        // Phase 1 runs a single instrument per adapter.
        _instrument = instruments[0];
        _sink = sink;
        _ct = ct;
        _reconciler = new BinanceReconciler(_instrument, sink, RequestSnapshot);

        return RunLoopAsync([_instrument], ct);
    }

    protected override Uri BuildEndpoint(IReadOnlyList<InstrumentId> instruments) =>
        new($"{StreamBaseUrl}/{BinanceSymbols.ToStream(instruments[0])}@depth");

    protected override Task OnConnectedAsync(
        ClientWebSocket socket, IReadOnlyList<InstrumentId> instruments, CancellationToken ct) =>
        // The raw <sym>@depth stream needs no subscribe message.
        Task.CompletedTask;

    protected override void OnConnected(IReadOnlyList<InstrumentId> instruments)
    {
        lock (_gate)
            _reconciler!.Reset();
    }

    protected override void OnMessage(ReadOnlySpan<byte> payload, long recvTsNanos)
    {
        // Allocation-free parse straight into the reused buffer (no DTO, no
        // string[][]). In steady state the span flows through to the book apply
        // without a copy.
        if (!BinanceDepthParser.TryParse(payload, _parseBuffer, out var count, out var firstId, out var finalId))
            return;

        lock (_gate)
            _reconciler!.OnDiff(firstId, finalId, _parseBuffer.AsSpan(0, count), recvTsNanos);
    }

    protected override void OnError(Exception exception) =>
        _log?.Invoke($"[Binance] {exception.GetType().Name}: {exception.Message}");

    // Invoked by the reconciler (under _gate) when it needs a fresh snapshot.
    // Fire-and-forget the REST fetch; it re-takes the lock when it completes.
    private void RequestSnapshot() => _ = FetchSnapshotAsync(_instrument, _ct);

    private async Task FetchSnapshotAsync(InstrumentId instrument, CancellationToken ct)
    {
        try
        {
            var snapshot = await _snapshotSource.FetchAsync(instrument, ct).ConfigureAwait(false);
            var recvTs = Clock.NowNanos();
            lock (_gate)
            {
                if (!ct.IsCancellationRequested)
                    _reconciler!.OnSnapshotReceived(snapshot, recvTs);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // The next reconnect or resync will request the snapshot again.
            _log?.Invoke($"[Binance] snapshot fetch failed: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsSnapshotSource && _snapshotSource is IDisposable disposable)
            disposable.Dispose();

        return ValueTask.CompletedTask;
    }
}
