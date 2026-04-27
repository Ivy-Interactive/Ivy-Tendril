using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Ivy.Tendril.Test.End2End.Configuration;
using Xunit.Abstractions;

namespace Ivy.Tendril.Test.End2End.Fixtures;

public class TendrilProcessFixture : IAsyncLifetime
{
    private Process? _tendrilProcess;
    private readonly List<string> _stdoutLines = new();
    private readonly List<string> _stderrLines = new();
    private readonly string _runId = Guid.NewGuid().ToString("N")[..12];

    public string TendrilHome { get; private set; } = "";
    public string TendrilPlans { get; private set; } = "";
    public string TendrilUrl { get; private set; } = "";

    public IReadOnlyList<string> StdoutLines => _stdoutLines;
    public IReadOnlyList<string> StderrLines => _stderrLines;

    public async Task InitializeAsync()
    {
        var settings = TestSettingsProvider.Get();

        TendrilHome = Path.Combine(Path.GetTempPath(), $"tendril-e2e-{_runId}");
        TendrilPlans = Path.Combine(TendrilHome, "Plans");
        Directory.CreateDirectory(TendrilHome);
        Directory.CreateDirectory(TendrilPlans);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{settings.TendrilProjectPath}\" -- --web --verbose --find-available-port",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.Environment["TENDRIL_HOME"] = TendrilHome;
        psi.Environment["TENDRIL_PLANS"] = TendrilPlans;

        _tendrilProcess = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Tendril process");

        TendrilUrl = await WaitForServerUrlAsync(
            TimeSpan.FromSeconds(settings.StartupTimeoutSeconds));
    }

    public async Task DisposeAsync()
    {
        if (_tendrilProcess is { HasExited: false })
        {
            try
            {
                _tendrilProcess.Kill(entireProcessTree: true);
                await _tendrilProcess.WaitForExitAsync();
            }
            catch
            {
                // Best-effort kill
            }
        }

        _tendrilProcess?.Dispose();

        await TryDeleteDirectoryAsync(TendrilHome);
    }

    private async Task<string> WaitForServerUrlAsync(TimeSpan timeout)
    {
        // Ivy framework outputs: "Ivy is running on https://localhost:5010 [PID]."
        // ASP.NET outputs: "Now listening on: https://localhost:5010"
        var urlPattern = new Regex(@"(?:running on|listening on:?)\s*(https?://[^\s\[\]]+)", RegexOptions.IgnoreCase);
        var tcs = new TaskCompletionSource<string>();
        using var cts = new CancellationTokenSource(timeout);

        cts.Token.Register(() =>
            tcs.TrySetException(new TimeoutException(
                $"Tendril did not start within {timeout.TotalSeconds}s. " +
                $"Stdout: {string.Join('\n', _stdoutLines.TakeLast(20))}\n" +
                $"Stderr: {string.Join('\n', _stderrLines.TakeLast(20))}")));

        _tendrilProcess!.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            _stdoutLines.Add(e.Data);

            var match = urlPattern.Match(e.Data);
            if (match.Success)
                tcs.TrySetResult(match.Groups[1].Value);
        };

        _tendrilProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                _stderrLines.Add(e.Data);
        };

        _tendrilProcess.BeginOutputReadLine();
        _tendrilProcess.BeginErrorReadLine();

        var url = await tcs.Task;

        // Poll until the server actually responds (bypass SSL for self-signed dev certs)
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(url);
                if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                    return url;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(500);
        }

        return url;
    }

    private static async Task TryDeleteDirectoryAsync(string path, int maxAttempts = 3)
    {
        if (!Directory.Exists(path)) return;

        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < maxAttempts - 1)
            {
                await Task.Delay(500 * (i + 1));
            }
            catch (UnauthorizedAccessException) when (i < maxAttempts - 1)
            {
                await Task.Delay(500 * (i + 1));
            }
        }
    }
}
