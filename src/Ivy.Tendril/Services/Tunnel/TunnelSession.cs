using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed partial class TunnelSession : IDisposable
{
    private static readonly TimeSpan UrlTimeout = TimeSpan.FromSeconds(30);

    private readonly string _binaryPath;
    private readonly int _port;
    private readonly ILogger _logger;
    private Process? _process;
    private TaskCompletionSource<string>? _urlTcs;

    public TunnelSession(string binaryPath, int port, ILogger logger)
    {
        _binaryPath = binaryPath;
        _port = port;
        _logger = logger;
    }

    public string? TunnelUrl { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    public async Task<string> StartAsync(CancellationToken ct = default)
    {
        _urlTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var psi = new ProcessStartInfo
        {
            FileName = _binaryPath,
            ArgumentList = { "tunnel", "--url", $"http://localhost:{_port}" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start cloudflared process");

        _process.ErrorDataReceived += OnStderrLine;
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(UrlTimeout);

        await using var reg = timeoutCts.Token.Register(() =>
            _urlTcs.TrySetException(new TimeoutException(
                $"Cloudflared did not produce a tunnel URL within {UrlTimeout.TotalSeconds}s")));

        var url = await _urlTcs.Task;
        TunnelUrl = url;
        _logger.LogInformation("Tunnel established: {Url}", url);
        return url;
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        if (_process is null) return;
        await _process.WaitForExitAsync(ct);
    }

    public void Stop()
    {
        if (_process is null or { HasExited: true }) return;

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        Stop();
        _process?.Dispose();
        _process = null;
    }

    private void OnStderrLine(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;

        _logger.LogDebug("[cloudflared] {Line}", e.Data);

        var url = ParseTunnelUrl(e.Data);
        if (url is not null)
            _urlTcs?.TrySetResult(url);
    }

    public static string? ParseTunnelUrl(string line)
    {
        var match = TunnelUrlRegex().Match(line);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelUrlRegex();
}
