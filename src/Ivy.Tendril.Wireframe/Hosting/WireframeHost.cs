using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Build;
using Ivy.Tendril.Wireframe.Project;
using Microsoft.AspNetCore.Http;

namespace Ivy.Tendril.Wireframe.Hosting;

public sealed class WireframeHostOptions
{
    /// <summary>
    /// How long a wireframe keeps building after its last page closes. Long enough to survive a
    /// React remount within the same plan, short enough that leaving the plan frees it promptly.
    /// </summary>
    public TimeSpan DisconnectGrace { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a wireframe whose page never opened a reload socket keeps its watcher.</summary>
    public TimeSpan UnwatchedGrace { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Live wireframe previews for Tendril plans, served from Tendril's own origin at
/// <c>/__wireframes/{scope}/{name}/</c>, where the scope is a plan id.
///
/// Only one plan is ever live. A person looks at one plan at a time, so opening a wireframe
/// from another plan first stops every watcher the previous plan had, and tells its pages to
/// stop listening. Closing a plan's last page stops its watchers too, after
/// <see cref="WireframeHostOptions.DisconnectGrace"/>. The last build stays on disk and keeps
/// serving, so a page that is still open (or reopened) shows the wireframe at once.
///
/// The frame is a real document on the same origin rather than something rendered inside the
/// Tendril page, for the same reason Studio's preview is: the wireframe gets its own React
/// instance from the vendor bundle and the same esbuild pipeline <c>screenshot</c> uses, so what a
/// reviewer sees is what the agent screenshotted.
/// </summary>
public sealed partial class WireframeHost : IWireframeSiteSource, IAsyncDisposable
{
    public const string RoutePrefix = "/__wireframes";
    public const string RoutePattern = RoutePrefix + "/{scope}/{name}";

    private readonly AssetCatalog _assets;
    private readonly Func<string, string, string?> _resolveRoot;
    private readonly WireframeHostOptions _options;

    // _gate serialises anything that starts or stops a watcher; _sync guards the fields that
    // request handlers read without waiting on a build.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private string? _activeScope;
    private string? _esbuild;

    private sealed class Entry(WireframeSite site)
    {
        public WireframeSite Site { get; } = site;
        public EsbuildWatcher? Watcher;
        public IAsyncDisposable? Tailwind;
        public WireframeStatus Status = WireframeStatus.Building;
        public int Sockets;
        public CancellationTokenSource? PendingStop;
    }

    /// <param name="assets">The embedded payload.</param>
    /// <param name="resolveRoot">
    /// Maps a (scope, name) pair to a wireframe project directory, or null when there is none.
    /// Both values have already passed <see cref="IsValidScope"/> and <see cref="IsValidName"/>.
    /// </param>
    public WireframeHost(AssetCatalog assets, Func<string, string, string?> resolveRoot, WireframeHostOptions? options = null)
    {
        _assets = assets;
        _resolveRoot = resolveRoot;
        _options = options ?? new WireframeHostOptions();
    }

    /// <summary>The plan whose wireframes may run, if any has been opened.</summary>
    public string? ActiveScope
    {
        get { lock (_sync) return _activeScope; }
    }

    /// <summary>How many esbuild watchers are running right now.</summary>
    public int RunningWatchers
    {
        get { lock (_sync) return _entries.Values.Count(e => e.Watcher is not null); }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex NamePattern();

    /// <summary>A wireframe name is a lowercase slug: it becomes a folder name and a URL segment.</summary>
    public static bool IsValidName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>A scope is a plan id: digits only.</summary>
    public static bool IsValidScope(string? scope) =>
        scope is { Length: > 0 and <= 9 } && scope.All(char.IsAsciiDigit);

    private static bool TryRoute(HttpContext ctx, out string scope, out string name)
    {
        var rawScope = ctx.Request.RouteValues["scope"] as string;
        name = ctx.Request.RouteValues["name"] as string ?? "";
        scope = "";

        if (!IsValidScope(rawScope) || !IsValidName(name)) return false;

        // 00123 and 123 are the same plan, and must be the same scope.
        scope = int.Parse(rawScope!, NumberStyles.None, CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public async ValueTask<WireframeSite?> OpenAsync(HttpContext context)
    {
        if (!TryRoute(context, out var scope, out var name)) return null;
        var (site, _) = await EnsureAsync(scope, name, context.RequestAborted);
        return site;
    }

    public async ValueTask<WireframeStatus> StatusAsync(HttpContext context)
    {
        if (!TryRoute(context, out var scope, out var name)) return WireframeStatus.Missing;
        var (_, status) = await EnsureAsync(scope, name, context.RequestAborted);
        return status;
    }

    public WireframeSite? Find(HttpContext context)
    {
        if (!TryRoute(context, out var scope, out var name)) return null;

        lock (_sync)
        {
            if (_activeScope == scope && _entries.TryGetValue(name, out var entry)) return entry.Site;
        }

        // A page left open on a plan that is no longer live can still load the files it was
        // built with; it just no longer hot reloads.
        var root = _resolveRoot(scope, name);
        if (root is null) return null;
        var project = WireframeProject.At(root);
        return project.Exists ? new WireframeSite(project, project.OutDir("tendril"), Hub: null, LiveReload: true) : null;
    }

    public async Task AcceptSocketAsync(HttpContext context, WebSocket socket)
    {
        Entry? entry = null;

        if (TryRoute(context, out var scope, out var name))
        {
            await _gate.WaitAsync(context.RequestAborted);
            try
            {
                lock (_sync)
                {
                    if (_activeScope == scope && _entries.TryGetValue(name, out var found))
                    {
                        entry = found;
                        CancelPendingStop(entry);
                        entry.Sockets++;
                    }
                }

                if (entry is { Watcher: null }) await StartAsync(entry);
            }
            finally
            {
                _gate.Release();
            }
        }

        if (entry?.Site.Hub is null)
        {
            // Not the live plan: tell the page to stop listening rather than let it reconnect
            // every half second, or reload itself and take the preview away from the plan that
            // is actually open.
            await SendStoppedAsync(socket);
            return;
        }

        try
        {
            await entry.Site.Hub.AddAsync(socket, context.RequestAborted);
        }
        finally
        {
            SocketClosed(entry);
        }
    }

    public void ReportPageError(HttpContext context, string kind, string detail)
    {
        // Nobody is watching a terminal for a plan preview. The page's own console has it.
    }

    private async Task<(WireframeSite? Site, WireframeStatus Status)> EnsureAsync(
        string scope, string name, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            string? active;
            lock (_sync) active = _activeScope;

            if (active != scope)
            {
                await TeardownAllAsync();
                lock (_sync) _activeScope = scope;
            }

            Entry? entry;
            lock (_sync) _entries.TryGetValue(name, out entry);

            if (entry is null)
            {
                var root = _resolveRoot(scope, name);
                if (root is null) return (null, WireframeStatus.Missing);

                var project = WireframeProject.At(root);
                if (!project.Exists) return (null, WireframeStatus.Missing);

                var utilityCss = WireframeConfig.Load(project).Tailwind == TailwindMode.Jit
                    ? TailwindCompiler.OutputPath(project)
                    : null;

                entry = new Entry(new WireframeSite(project, project.OutDir("tendril"), new LiveReloadHub(), LiveReload: true)
                {
                    UtilityCssPath = utilityCss,
                });
                lock (_sync) _entries[name] = entry;
            }

            if (entry.Watcher is null) await StartAsync(entry);

            lock (_sync)
            {
                if (entry.Sockets == 0) ScheduleStop(entry, _options.UnwatchedGrace);
            }

            return (entry.Site, entry.Status);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Builds and starts watching. Deliberately not cancellable by the request that asked: a
    /// reviewer who navigates away mid-build has still paid for most of it, and the next open
    /// should find it done.
    /// </summary>
    private async Task StartAsync(Entry entry)
    {
        var project = entry.Site.Project;
        entry.Status = WireframeStatus.Building;

        try
        {
            var scaffolder = new ProjectScaffolder(_assets);
            if (scaffolder.NeedsRefresh(project)) scaffolder.MaterializeWorkspace(project);

            _esbuild ??= await new EsbuildProvisioner().ResolveAsync();

            if (entry.Site.UtilityCssPath is not null)
            {
                var tailwind = await new TailwindProvisioner().ResolveAsync();
                entry.Tailwind = await new TailwindCompiler(tailwind, _assets, project).StartWatchAsync();
            }

            var watcher = new EsbuildWatcher(_esbuild, project, VendorManifest.Load(_assets));
            watcher.BuildCompleted += result => OnBuilt(entry, watcher, result);
            lock (_sync) entry.Watcher = watcher;

            var first = await watcher.StartAsync(entry.Site.OutDir);
            entry.Status = first.Success ? WireframeStatus.Running : BuildFailed;
        }
        catch (Exception ex)
        {
            entry.Status = WireframeStatus.Failed(ex.Message);
            await StopWatcherAsync(entry);
        }
    }

    /// <summary>
    /// A reviewer is shown that the wireframe does not build, never esbuild's diagnostics:
    /// those quote source code, and the plan view does not show code.
    /// </summary>
    private static readonly WireframeStatus BuildFailed =
        WireframeStatus.Failed("The latest version of this wireframe does not build.");

    private void OnBuilt(Entry entry, EsbuildWatcher watcher, BuildResult result)
    {
        lock (_sync)
        {
            // A watcher that has been stopped can still deliver one late event.
            if (!ReferenceEquals(entry.Watcher, watcher)) return;
        }

        entry.Status = result.Success ? WireframeStatus.Running : BuildFailed;

        if (result.Success)
            entry.Site.Hub?.Broadcast(new { type = "reload" });
        else
            entry.Site.Hub?.Broadcast(new { type = "error", text = result.Output, location = result.FirstLocation });
    }

    private void SocketClosed(Entry entry)
    {
        lock (_sync)
        {
            entry.Sockets = Math.Max(0, entry.Sockets - 1);
            if (entry.Sockets == 0) ScheduleStop(entry, _options.DisconnectGrace);
        }
    }

    /// <summary>Call with <see cref="_sync"/> held.</summary>
    private void ScheduleStop(Entry entry, TimeSpan delay)
    {
        CancelPendingStop(entry);
        var cts = new CancellationTokenSource();
        entry.PendingStop = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                await _gate.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                bool idle;
                lock (_sync) idle = entry.Sockets == 0 && !cts.IsCancellationRequested;
                if (idle) await StopWatcherAsync(entry);
            }
            finally
            {
                _gate.Release();
            }
        });
    }

    /// <summary>Call with <see cref="_sync"/> held.</summary>
    private static void CancelPendingStop(Entry entry)
    {
        entry.PendingStop?.Cancel();
        entry.PendingStop = null;
    }

    /// <summary>Call with <see cref="_gate"/> held.</summary>
    private async Task StopWatcherAsync(Entry entry)
    {
        EsbuildWatcher? watcher;
        IAsyncDisposable? tailwind;
        lock (_sync)
        {
            watcher = entry.Watcher;
            tailwind = entry.Tailwind;
            entry.Watcher = null;
            entry.Tailwind = null;
        }

        if (watcher is not null) await watcher.DisposeAsync();
        if (tailwind is not null) await tailwind.DisposeAsync();
    }

    /// <summary>Stops every wireframe of the live plan. Call with <see cref="_gate"/> held.</summary>
    private async Task TeardownAllAsync()
    {
        List<Entry> entries;
        lock (_sync)
        {
            entries = [.. _entries.Values];
            _entries.Clear();
            _activeScope = null;
            foreach (var entry in entries) CancelPendingStop(entry);
        }

        foreach (var entry in entries)
        {
            if (entry.Site.Hub is { } hub)
            {
                await hub.BroadcastAsync(new { type = "stopped" });
                await hub.DisposeAsync();
            }
            await StopWatcherAsync(entry);
        }
    }

    private static async Task SendStoppedAsync(WebSocket socket)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "stopped" }));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "not the live plan", CancellationToken.None);
        }
        catch (Exception e) when (e is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The page went away first.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await TeardownAllAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
