using System.Text.Json;
using Ivy.Tendril.Wireframe.Browser;

namespace Ivy.Tendril.Wireframe.Screenshot;

public sealed record ScreenshotOptions(
    string Url,
    string OutputPath,
    int Width,
    int Height,
    double Scale,
    bool FullPage,
    bool Transparent,
    TimeSpan Timeout,
    string? BrowserPath);

public sealed record ScreenshotResult(
    string Path,
    int PixelWidth,
    int PixelHeight,
    long Bytes,
    bool UsedReadyHook,
    string? Warning);

/// <summary>
/// Drives a headless browser over CDP to capture a wireframe.
/// </summary>
public sealed class ScreenshotRunner
{
    /// <summary>Skia's maximum texture dimension; past this a capture silently truncates.</summary>
    private const int MaxCaptureHeight = 16384;

    public async Task<ScreenshotResult> CaptureAsync(ScreenshotOptions options, CancellationToken ct = default)
    {
        await using var browser = await BrowserProcess.LaunchAsync(options.BrowserPath, ct);
        await using var cdp = await CdpConnection.ConnectAsync(browser.WebSocketUrl, ct);

        // Flat session: carry sessionId on every message rather than nesting messages
        // inside Target.sendMessageToTarget.
        var target = await cdp.SendAsync("Target.createTarget", new { url = "about:blank" }, ct: ct);
        var targetId = target.GetProperty("targetId").GetString();

        var attached = await cdp.SendAsync("Target.attachToTarget",
            new { targetId, flatten = true }, ct: ct);
        var session = attached.GetProperty("sessionId").GetString();

        await cdp.SendAsync("Page.enable", sessionId: session, ct: ct);
        await cdp.SendAsync("Runtime.enable", sessionId: session, ct: ct);

        // Everything below must be in place BEFORE the first paint.
        await cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument",
            new { source = DeterminismPayload.Script }, session, ct);

        await cdp.SendAsync("Emulation.setDeviceMetricsOverride", new
        {
            width = options.Width,
            height = options.Height,
            deviceScaleFactor = options.Scale,
            mobile = false,
        }, session, ct);

        await cdp.SendAsync("Emulation.setEmulatedMedia", new
        {
            media = "screen",
            features = new[]
            {
                new { name = "prefers-reduced-motion", value = "reduce" },
                new { name = "prefers-color-scheme", value = "light" },
                new { name = "forced-colors", value = "none" },
            },
        }, session, ct);

        if (!options.Transparent)
        {
            // --color-paper. Without this an app that forgets a background emits a
            // transparent PNG, which reads as broken in most viewers.
            await cdp.SendAsync("Emulation.setDefaultBackgroundColorOverride",
                new { color = new { r = 0xFD, g = 0xFC, b = 0xF7, a = 1 } }, session, ct);
        }

        // Belt and braces: the shipped CSS has its Google Fonts @import stripped, but a
        // hand-edited index.html could add one back, and a slow font request would stall
        // document.fonts.ready and therefore the whole readiness chain.
        await cdp.SendAsync("Network.enable", sessionId: session, ct: ct);
        await cdp.SendAsync("Network.setBlockedURLs", new
        {
            urls = new[] { "*://fonts.googleapis.com/*", "*://fonts.gstatic.com/*" },
        }, session, ct);

        var loaded = cdp.WaitForEventAsync("Page.loadEventFired", options.Timeout, ct);
        await cdp.SendAsync("Page.navigate", new { url = options.Url }, session, ct);
        await loaded;

        // Freeze anything the media override missed: this is what actually stops the
        // library's keyframes, plus any CSS transitions and Web Animations.
        await cdp.SendAsync("Animation.enable", sessionId: session, ct: ct);
        await cdp.SendAsync("Animation.setPlaybackRate", new { playbackRate = 0 }, session, ct);

        var (ready, hook, reason, deterministic) = await PollReadinessAsync(cdp, session!, options.Timeout, ct);

        string? warning = null;
        if (!ready)
            warning = $"the page never signalled ready ({reason}); captured anyway";
        else if (!hook)
            warning = "no window.__wireframe hook; used the heuristic probe. " +
                      "Call signalWireframeReady() from main.tsx for reliable captures";
        else if (!deterministic)
            warning = "SketchProvider has deterministic={false}; repeated screenshots will differ";

        // One more paint, in case polling itself caused work.
        await EvaluateAsync(cdp, session!, DeterminismPayload.SettlePaint, awaitPromise: true, ct);

        var captureHeight = options.Height;
        if (options.FullPage)
        {
            var metrics = await cdp.SendAsync("Page.getLayoutMetrics", sessionId: session, ct: ct);
            var contentHeight = (int)Math.Ceiling(
                metrics.GetProperty("cssContentSize").GetProperty("height").GetDouble());

            captureHeight = Math.Max(options.Height, contentHeight);
            if (captureHeight > MaxCaptureHeight)
            {
                warning = $"page is {captureHeight}px tall; clamped to {MaxCaptureHeight}px " +
                          "(the maximum a browser can rasterize)";
                captureHeight = MaxCaptureHeight;
            }
        }

        var shot = await cdp.SendAsync("Page.captureScreenshot", new
        {
            format = "png",
            fromSurface = true,
            optimizeForSpeed = false,
            captureBeyondViewport = options.FullPage,
            clip = new
            {
                x = 0.0,
                y = 0.0,
                width = (double)options.Width,
                height = (double)captureHeight,
                scale = 1.0,
            },
        }, session, ct);

        var bytes = Convert.FromBase64String(shot.GetProperty("data").GetString()!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath)!);
        await File.WriteAllBytesAsync(options.OutputPath, bytes, ct);

        return new ScreenshotResult(
            options.OutputPath,
            (int)(options.Width * options.Scale),
            (int)(captureHeight * options.Scale),
            bytes.Length,
            hook,
            warning);
    }

    private static async Task<(bool Ready, bool Hook, string? Reason, bool Deterministic)>
        PollReadinessAsync(CdpConnection cdp, string session, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var hook = false;
        string? reason = "no probe result";
        var deterministic = true;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var value = await EvaluateAsync(cdp, session, DeterminismPayload.ReadinessProbe, false, ct);
            if (value.ValueKind == JsonValueKind.Object)
            {
                hook = value.TryGetProperty("hook", out var h) && h.GetBoolean();
                reason = value.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                    ? r.GetString()
                    : null;
                deterministic = !value.TryGetProperty("deterministic", out var d) || d.GetBoolean();

                if (value.TryGetProperty("ready", out var ready) && ready.GetBoolean())
                    return (true, hook, null, deterministic);
            }

            await Task.Delay(50, ct);
        }

        return (false, hook, reason, deterministic);
    }

    private static async Task<JsonElement> EvaluateAsync(
        CdpConnection cdp, string session, string expression, bool awaitPromise, CancellationToken ct)
    {
        var result = await cdp.SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise,
        }, session, ct);

        return result.TryGetProperty("result", out var inner) && inner.TryGetProperty("value", out var value)
            ? value
            : default;
    }
}
