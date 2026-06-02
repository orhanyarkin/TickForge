using System;
using System.Buffers;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Core.Abstractions;
using TickForge.Core.Time;

namespace TickForge.Feeds.Common;

/// <summary>
/// Shared WebSocket transport for feed adapters: it owns the connection, a
/// reconnect loop with capped exponential backoff, and a frame-read loop that
/// reassembles fragmented messages into a single contiguous payload.
///
/// The receive timestamp (<see cref="Clock.NowNanos"/>) is captured the instant
/// the socket hands back the final frame of a message — this is the reference
/// point for internal tick-to-process latency, so it must be taken before any
/// parsing work.
///
/// Subclasses implement the exchange specifics: the endpoint, the subscribe
/// handshake, and message handling.
/// </summary>
public abstract class WebSocketFeedBase
{
    private const int InitialBufferSize = 16 * 1024;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private ClientWebSocket? _activeSocket;

    /// <summary>Build the WebSocket endpoint for the requested instruments.</summary>
    protected abstract Uri BuildEndpoint(IReadOnlyList<InstrumentId> instruments);

    /// <summary>
    /// Called once after each successful connect, before the read loop starts.
    /// Use it to send the subscribe handshake when the exchange requires one.
    /// </summary>
    protected abstract Task OnConnectedAsync(
        ClientWebSocket socket, IReadOnlyList<InstrumentId> instruments, CancellationToken ct);

    /// <summary>
    /// Handle one complete message. <paramref name="recvTsNanos"/> is the
    /// monotonic timestamp captured the instant the frame was read.
    /// </summary>
    protected abstract void OnMessage(ReadOnlySpan<byte> payload, long recvTsNanos);

    /// <summary>
    /// Called when a fresh connection is established (initial or after a
    /// reconnect) so the subclass can reset its consistency state and re-bootstrap.
    /// </summary>
    protected abstract void OnConnected(IReadOnlyList<InstrumentId> instruments);

    /// <summary>
    /// Connect-read-reconnect until cancelled. Each iteration opens a socket,
    /// runs the handshake, then pumps messages; any failure backs off and retries.
    /// </summary>
    protected async Task RunLoopAsync(IReadOnlyList<InstrumentId> instruments, CancellationToken ct)
    {
        var backoff = InitialBackoff;

        while (!ct.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            _activeSocket = socket;
            try
            {
                await socket.ConnectAsync(BuildEndpoint(instruments), ct).ConfigureAwait(false);
                backoff = InitialBackoff; // reset on a successful connect
                OnConnected(instruments);
                await OnConnectedAsync(socket, instruments, ct).ConfigureAwait(false);
                await ReadLoopAsync(socket, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError(ex);
                await DelayBackoffAsync(backoff, ct).ConfigureAwait(false);
                backoff = NextBackoff(backoff);
            }
            finally
            {
                _activeSocket = null;
            }
        }
    }

    /// <summary>
    /// Force the current connection to drop so the loop reconnects and re-runs
    /// the subscribe handshake. Used to re-subscribe after a sequence gap on
    /// exchanges (like Coinbase) whose recovery is "re-subscribe for a fresh
    /// snapshot". Aborting leaves the socket non-open, so the read loop exits
    /// cleanly and reconnects without backoff.
    /// </summary>
    protected void RequestReconnect() => _activeSocket?.Abort();

    private async Task ReadLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var count = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (count == buffer.Length)
                        buffer = Grow(buffer);

                    result = await socket
                        .ReceiveAsync(buffer.AsMemory(count), ct)
                        .ConfigureAwait(false);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    count += result.Count;
                }
                while (!result.EndOfMessage);

                // Capture the receive timestamp the instant the full frame is in hand.
                var recvTs = Clock.NowNanos();
                OnMessage(buffer.AsSpan(0, count), recvTs);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static byte[] Grow(byte[] buffer)
    {
        var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
        Array.Copy(buffer, bigger, buffer.Length);
        ArrayPool<byte>.Shared.Return(buffer);
        return bigger;
    }

    private static TimeSpan NextBackoff(TimeSpan current)
    {
        var doubled = TimeSpan.FromTicks(current.Ticks * 2);
        return doubled > MaxBackoff ? MaxBackoff : doubled;
    }

    private static async Task DelayBackoffAsync(TimeSpan backoff, CancellationToken ct)
    {
        try
        {
            await Task.Delay(backoff, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is handled by the outer loop condition.
        }
    }

    /// <summary>Hook for logging a non-fatal connection error before backoff.</summary>
    protected virtual void OnError(Exception exception)
    {
    }
}
