using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Ivy.Tendril.Wireframe.Browser;

/// <summary>
/// A minimal Chrome DevTools Protocol client -- enough for "navigate, wait, capture".
///
/// Hand-rolled rather than taking a dependency: Playwright would mean shipping a private
/// Node runtime inside a tool whose headline promise is "no node", and PuppeteerSharp is
/// a megabyte of assembly plus logging dependencies for the nine protocol calls used here.
/// </summary>
public sealed class CdpConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;
    private Task? _readLoop;

    public static async Task<CdpConnection> ConnectAsync(string webSocketUrl, CancellationToken ct = default)
    {
        var connection = new CdpConnection();
        await connection._socket.ConnectAsync(new Uri(webSocketUrl), ct);
        connection._readLoop = Task.Run(() => connection.ReadLoopAsync(connection._cts.Token), CancellationToken.None);
        return connection;
    }

    /// <summary>
    /// Reads frames off the socket, reassembling fragments.
    ///
    /// The EndOfMessage loop is essential rather than defensive: Page.captureScreenshot
    /// returns a base64 PNG inline, and a 2880x1800 sketch page arrives as several
    /// megabytes across dozens of frames. Parsing per-frame yields intermittent JSON errors
    /// that only ever show up on large pages.
    /// </summary>
    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new MemoryStream();

        try
        {
            while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                Dispatch(message.ToArray());
            }
        }
        catch (Exception e) when (e is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Connection closed; fail anything still waiting so callers do not hang.
            foreach (var pending in _pending.Values)
                pending.TrySetException(new IOException("The DevTools connection closed."));
        }
    }

    private void Dispatch(byte[] payload)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(payload); }
        catch (JsonException) { return; }

        var root = doc.RootElement;

        if (root.TryGetProperty("id", out var idElement)
            && _pending.TryRemove(idElement.GetInt32(), out var completion))
        {
            if (root.TryGetProperty("error", out var error))
            {
                completion.TrySetException(new InvalidOperationException(
                    $"CDP error: {error.GetRawText()}"));
            }
            else
            {
                // Clone: the JsonDocument is disposed when this method returns.
                completion.TrySetResult(root.TryGetProperty("result", out var r)
                    ? r.Clone()
                    : default);
            }
            doc.Dispose();
            return;
        }

        if (root.TryGetProperty("method", out var method))
        {
            EventReceived?.Invoke(method.GetString() ?? "",
                root.TryGetProperty("params", out var p) ? p.Clone() : default);
        }

        doc.Dispose();
    }

    public event Action<string, JsonElement>? EventReceived;

    public async Task<JsonElement> SendAsync(
        string method, object? parameters = null, string? sessionId = null, CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var envelope = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null) envelope["params"] = parameters;
        if (sessionId is not null) envelope["sessionId"] = sessionId;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        return await completion.Task;
    }

    /// <summary>Waits for one CDP event, or throws on timeout.</summary>
    public async Task<JsonElement> WaitForEventAsync(string method, TimeSpan timeout, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(string name, JsonElement payload)
        {
            if (name == method) completion.TrySetResult(payload);
        }

        EventReceived += Handler;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            using var registration = timeoutCts.Token.Register(() =>
                completion.TrySetException(new TimeoutException($"Timed out waiting for {method}.")));
            return await completion.Task;
        }
        finally
        {
            EventReceived -= Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch
        {
            // Best effort.
        }

        if (_readLoop is not null)
        {
            try { await _readLoop; } catch { /* already reported */ }
        }

        _socket.Dispose();
        _cts.Dispose();
    }
}
