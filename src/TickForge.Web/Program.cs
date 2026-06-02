using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using TickForge.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MarketDataService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketDataService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

// Live state over WebSocket: push a fresh snapshot ~10×/second to each client.
app.Map("/ws", async (HttpContext context, MarketDataService market) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var ct = context.RequestAborted;
    try
    {
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var payload = market.BuildStateJson();
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected or the app is shutting down.
    }
    catch (WebSocketException)
    {
        // Client went away mid-send; nothing to do.
    }
});

// Same payload over plain HTTP — handy for quick checks and as a non-WS fallback.
app.MapGet("/api/state", (MarketDataService market) =>
    Results.Bytes(market.BuildStateJson(), "application/json"));

app.Run();
