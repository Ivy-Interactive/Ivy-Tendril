using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Tunnel;

public sealed partial class TunnelSession : IDisposable
{
    private static readonly TimeSpan UrlTimeout = TimeSpan.FromSeconds(60);

    private readonly string _binaryPath;
    private readonly string _originUrl;
    private readonly ILogger _logger;
    private Process? _process;
    private TaskCompletionSource<string>? _urlTcs;

    public TunnelSession(string binaryPath, string originUrl, ILogger logger)
    {
        _binaryPath = binaryPath;
        _originUrl = originUrl;
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
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("tunnel");
        psi.ArgumentList.Add("--protocol");
        psi.ArgumentList.Add("http2");
        psi.ArgumentList.Add("--url");
        psi.ArgumentList.Add(_originUrl);
        if (_originUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add("--no-tls-verify");

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Cloudflare process");

        // Tie the process lifetime to ours: if Tendril dies without a graceful shutdown
        // (crash, console close, forced kill), the OS still terminates cloudflared instead
        // of leaving it orphaned across sessions.
        if (!ChildProcessTracker.AddProcess(_process) && OperatingSystem.IsWindows())
            _logger.LogDebug("cloudflared process not tracked by job object (PID {Pid})", _process.Id);

        _process.ErrorDataReceived += OnStderrLine;
        _process.OutputDataReceived += OnStderrLine;
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(UrlTimeout);

        await using var reg = timeoutCts.Token.Register(() =>
        {
            string recentOutput;
            lock (_logLock)
            {
                recentOutput = _recentLogs.Count > 0
                    ? string.Join(" | ", _recentLogs.TakeLast(3))
                    : "No output received from cloudflared";
            }
            _urlTcs.TrySetException(new TimeoutException(
                $"Cloudflare did not produce a tunnel URL within {UrlTimeout.TotalSeconds}s ({recentOutput})"));
        });

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

    private readonly List<string> _recentLogs = [];
    private readonly object _logLock = new();

    private void OnStderrLine(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;

        _logger.LogDebug("[cloudflared] {Line}", e.Data);

        lock (_logLock)
        {
            if (_recentLogs.Count >= 20)
                _recentLogs.RemoveAt(0);
            _recentLogs.Add(e.Data);
        }

        var url = ParseTunnelUrl(e.Data);
        if (url is not null)
            _urlTcs?.TrySetResult(url);
    }

    public static string? ParseTunnelUrl(string line)
    {
        var match = TunnelUrlRegex().Match(line);
        if (match.Success)
        {
            var url = match.Value;
            if (url.Equals("https://api.trycloudflare.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            return url;
        }
        return null;
    }

    [GeneratedRegex(@"https://[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)]
    private static partial Regex TunnelUrlRegex();
}
