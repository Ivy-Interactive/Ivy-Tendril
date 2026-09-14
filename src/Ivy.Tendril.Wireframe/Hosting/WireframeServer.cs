using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Project;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Wireframe.Hosting;

public sealed record ServerOptions(
    WireframeProject Project,
    string OutDir,
    bool LiveReload,
    int Port = 0)
{
    /// <summary>
    /// When set, this file is served as the utility sheet instead of the embedded superset.
    /// Used by --tailwind jit, where the Tailwind CLI generates the sheet.
    /// </summary>
    public string? UtilityCssPath { get; init; }
}

/// <summary>
/// The standalone dev server behind <c>tendril wireframe serve</c> and <c>screenshot</c>. Binds a
/// free loopback port and serves one wireframe at its root, through the same routes Tendril's
/// plan previews use.
/// </summary>
public sealed class WireframeServer(AssetCatalog assets, ServerOptions options) : IAsyncDisposable, IWireframeSiteSource
{
    private WebApplication? _app;
    private readonly LiveReloadHub _hub = new();
    private WireframeSite? _site;

    public LiveReloadHub Hub => _hub;
    public string Url { get; private set; } = "";

    /// <summary>Raised when the page reports an uncaught error, so serve can print it.</summary>
    public event Action<string, string>? PageError;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _site = new WireframeSite(options.Project, options.OutDir, _hub, options.LiveReload)
        {
            UtilityCssPath = options.UtilityCssPath,
        };

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);
        builder.Services.AddSingleton(assets);

        builder.WebHost.ConfigureKestrel(k =>
        {
            // Listen(Loopback, 0), not ListenLocalhost(0): "localhost" means binding both
            // 127.0.0.1 and [::1], which with port 0 would allocate two DIFFERENT ephemeral
            // ports. Kestrel rejects that combination outright.
            //
            // Loopback-only is also deliberate: unreleased wireframes have no business
            // being reachable from the network.
            k.Listen(IPAddress.Loopback, options.Port);
            k.AddServerHeader = false;
        });

        var app = builder.Build();
        app.UseWebSockets();
        app.MapWireframePayload(assets);
        app.MapWireframeSite("", assets, this);

        await app.StartAsync(ct);
        _app = app;

        // The only race-free way to learn the port: read it back after StartAsync.
        // Probing with a TcpListener and closing it is a TOCTOU bug on Windows.
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report a bound address.");

        // Report 127.0.0.1 rather than "localhost", which can resolve to ::1 first and fail.
        Url = address.Replace("[::]", "127.0.0.1").Replace("localhost", "127.0.0.1");
    }

    ValueTask<WireframeSite?> IWireframeSiteSource.OpenAsync(HttpContext context) => ValueTask.FromResult(_site);

    WireframeSite? IWireframeSiteSource.Find(HttpContext context) => _site;

    Task IWireframeSiteSource.AcceptSocketAsync(HttpContext context, WebSocket socket) =>
        _hub.AddAsync(socket, context.RequestAborted);

    ValueTask<WireframeStatus> IWireframeSiteSource.StatusAsync(HttpContext context) =>
        ValueTask.FromResult(WireframeStatus.Running);

    void IWireframeSiteSource.ReportPageError(HttpContext context, string kind, string detail) =>
        PageError?.Invoke(kind, detail);

    public async ValueTask DisposeAsync()
    {
        await _hub.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }
}

/// <summary>Fans build events out to every connected browser.</summary>
public sealed class LiveReloadHub : IAsyncDisposable
{
    private readonly List<WebSocket> _sockets = [];
    private readonly Lock _gate = new();

    public async Task AddAsync(WebSocket socket, CancellationToken ct)
    {
        lock (_gate) _sockets.Add(socket);
        try
        {
            // Hold the request open; we only ever push. Reading also lets us notice close.
            var buffer = new byte[256];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType != WebSocketMessageType.Close) continue;

                // Answer the page's close before the request ends. Returning without a reply
                // drops the connection mid-handshake, which the page reports as an abnormal close.
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
                catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
                {
                    // Already gone.
                }
                break;
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException)
        {
            // Browser navigated away or the server is shutting down.
        }
        finally
        {
            lock (_gate) _sockets.Remove(socket);
        }
    }

    public void Broadcast(object message) => _ = BroadcastAsync(message);

    /// <summary>Like <see cref="Broadcast"/>, but finishes sending before it returns, so a close
    /// that follows cannot race the message.</summary>
    public Task BroadcastAsync(object message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        WebSocket[] targets;
        lock (_gate) targets = [.. _sockets];

        return Task.WhenAll(targets
            .Where(socket => socket.State == WebSocketState.Open)
            .Select(socket => SendSafeAsync(socket, bytes)));
    }

    private static async Task SendSafeAsync(WebSocket socket, byte[] bytes)
    {
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The client vanished between the state check and the send.
        }
    }

    public async ValueTask DisposeAsync()
    {
        WebSocket[] targets;
        lock (_gate) { targets = [.. _sockets]; _sockets.Clear(); }

        // CloseOutputAsync, not CloseAsync: CloseAsync waits for the client's reply, and a
        // client that is not reading never sends one. WireframeHost closes these while it holds
        // the lock every preview request waits on, so one silent page would stall them all.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        foreach (var socket in targets)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "shutting down", timeout.Token);
            }
            catch
            {
                // Best effort on shutdown.
            }
        }
    }
}
