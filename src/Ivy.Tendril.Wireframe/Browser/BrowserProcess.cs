using System.Diagnostics;

namespace Ivy.Tendril.Wireframe.Browser;

/// <summary>
/// Launches a headless browser with remote debugging and cleans up after it.
/// </summary>
public sealed class BrowserProcess : IAsyncDisposable
{
    private Process? _process;
    private string? _userDataDir;

    public string WebSocketUrl { get; private set; } = "";
    public BrowserInfo Info { get; private set; } = null!;

    /// <summary>
    /// Flags chosen for determinism as much as for headlessness -- see DeterminismPayload
    /// for the rest of the story. The rendering flags pin Skia so repeated runs on one
    /// machine are byte-identical.
    /// </summary>
    private static IEnumerable<string> Flags(string userDataDir) =>
    [
        "--headless=new",
        "--remote-debugging-port=0",
        $"--user-data-dir={userDataDir}",
        "--no-first-run",
        "--no-default-browser-check",
        "--no-pings",
        "--mute-audio",
        "--disable-extensions",
        "--disable-component-extension-with-background-pages",
        "--disable-background-networking",
        "--disable-sync",
        "--disable-default-apps",
        "--disable-features=Translate,BackForwardCache,AcceptCHFrame,MediaRouter,OptimizationHints",
        "--metrics-recording-only",
        "--disable-dev-shm-usage",
        // An overflowing page otherwise shifts content ~15px left behind a scrollbar.
        "--hide-scrollbars",
        // Pin rasterization: SketchProvider mounts feTurbulence/feDisplacementMap filters,
        // and GPU vs CPU Skia produce slightly different pixels for SVG filters.
        "--disable-gpu",
        "--force-color-profile=srgb",
        "--disable-lcd-text",
        "--font-render-hinting=none",
        "about:blank",
    ];

    public static async Task<BrowserProcess> LaunchAsync(
        string? browserPath = null, CancellationToken ct = default)
    {
        var browser = new BrowserProcess { Info = BrowserLocator.Locate(browserPath) };

        // A fresh profile is NOT optional. Without it Chrome may hand the command off to an
        // already-running instance, exit 0 immediately, and never write DevToolsActivePort
        // -- which presents as a baffling "the browser exited successfully" hang.
        browser._userDataDir = Directory.CreateTempSubdirectory("wireframe-cdp-").FullName;

        var psi = new ProcessStartInfo(browser.Info.Path)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var flag in Flags(browser._userDataDir)) psi.ArgumentList.Add(flag);

        browser._process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {browser.Info.Name}.");

        // Drain the pipes so a full buffer cannot stall the browser.
        _ = Task.Run(async () =>
        {
            try { await browser._process.StandardOutput.ReadToEndAsync(ct); } catch { }
        }, CancellationToken.None);
        _ = Task.Run(async () =>
        {
            try { await browser._process.StandardError.ReadToEndAsync(ct); } catch { }
        }, CancellationToken.None);

        browser.WebSocketUrl = await browser.ReadDevToolsUrlAsync(ct);
        return browser;
    }

    /// <summary>
    /// Reads the endpoint from &lt;user-data-dir&gt;/DevToolsActivePort, which is the
    /// documented contract. Scraping "DevTools listening on ws://..." from stderr is
    /// unreliable as a primary source because of buffering and Edge's occasional
    /// suppression of it.
    /// </summary>
    private async Task<string> ReadDevToolsUrlAsync(CancellationToken ct)
    {
        var portFile = Path.Combine(_userDataDir!, "DevToolsActivePort");
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_process!.HasExited)
                throw new InvalidOperationException(
                    $"{Info.Name} exited with code {_process.ExitCode} before DevTools became available. " +
                    "An enterprise policy may be blocking headless mode or remote debugging; " +
                    "try --browser with a different Chromium install.");

            if (File.Exists(portFile))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(portFile, ct);
                    if (lines.Length >= 2 && int.TryParse(lines[0], out var port))
                        return $"ws://127.0.0.1:{port}{lines[1]}";
                }
                catch (IOException)
                {
                    // Chrome is still writing it; retry.
                }
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException(
            $"{Info.Name} did not expose a DevTools endpoint within 30s ({portFile} never appeared).");
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Already gone.
            }
        }
        _process?.Dispose();

        if (_userDataDir is not null)
        {
            // Chrome can hold files briefly after exit; a couple of retries avoids leaving
            // a temp profile behind on Windows.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.Delete(_userDataDir, recursive: true);
                    break;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(150);
                }
            }
        }
    }
}
