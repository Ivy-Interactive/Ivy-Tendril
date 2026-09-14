using System.Diagnostics;
using System.Text;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Project;

namespace Ivy.Tendril.Wireframe.Build;

/// <summary>Outcome of one esbuild cycle. <see cref="Output"/> is esbuild's own stderr,
/// preserved verbatim -- its caret diagnostics are better than anything we would render.</summary>
public sealed record BuildResult(bool Success, string Output)
{
    /// <summary>First "file:line:col" esbuild reported, for the overlay header.</summary>
    public string? FirstLocation
    {
        get
        {
            foreach (var line in Output.Split('\n'))
            {
                var t = line.Trim();
                var m = System.Text.RegularExpressions.Regex.Match(t, @"^(.+?):(\d+):(\d+):$");
                if (m.Success) return t.TrimEnd(':');
            }
            return null;
        }
    }
}

/// <summary>
/// Runs the esbuild binary over the user's src/.
///
/// Only the agent's own code is bundled: react, react-dom and tendril-wireframes are
/// marked external and resolved in the browser through the import map, so a rebuild never
/// walks the dependency tree and lands in the 5-15 ms range.
/// </summary>
public sealed class EsbuildBundler(string esbuildPath, WireframeProject project, VendorManifest vendor)
{
    /// <summary>
    /// Arguments shared by the one-shot and watch modes.
    ///
    /// Note the externals are an explicit allowlist rather than --packages=external: with
    /// an allowlist, `import "recharts"` fails the BUILD with a clear message. With
    /// --packages=external it would compile happily and then die in the browser with an
    /// opaque import-map miss.
    /// </summary>
    public List<string> BuildArguments(string outDir, bool sourcemap = true)
    {
        var args = new List<string>
        {
            project.EntryPoint,
            "--bundle",
            $"--outdir={outDir}",
            "--format=esm",
            "--target=es2022",
            "--jsx=automatic",
            "--jsx-import-source=react",
            "--loader:.svg=dataurl",
            "--loader:.png=dataurl",
            "--loader:.jpg=dataurl",
            "--loader:.webp=dataurl",
            "--log-level=info",
            "--color=false",
            "--entry-names=bundle",
        };

        if (sourcemap) args.Add("--sourcemap=linked");
        args.AddRange(vendor.ExternalSpecifiers.Select(s => $"--external:{s}"));
        return args;
    }

    /// <summary>One clean synchronous build. Used by `screenshot`, which must never observe
    /// a half-written bundle.</summary>
    public async Task<BuildResult> BuildOnceAsync(string outDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);

        var psi = new ProcessStartInfo(esbuildPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = project.Root,
        };
        foreach (var a in BuildArguments(outDir)) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start esbuild at {esbuildPath}");

        var stderr = await p.StandardError.ReadToEndAsync(ct);
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);

        var output = string.Join("\n", new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new BuildResult(p.ExitCode == 0, output.Trim());
    }
}

/// <summary>
/// Supervises a long-lived <c>esbuild --watch</c> child.
///
/// This is deliberately NOT a FileSystemWatcher over src/. esbuild's watcher is
/// import-graph aware (it rebuilds only when a file the bundle actually imports changes),
/// it already handles the write-temp-then-rename storm that editors produce and that a
/// hand-rolled debounce gets wrong, and it keeps its parse cache in-process -- 5-15 ms per
/// rebuild versus ~60 ms of process startup on every save.
/// </summary>
public sealed class EsbuildWatcher(
    string esbuildPath,
    WireframeProject project,
    VendorManifest vendor) : IAsyncDisposable
{
    private Process? _process;
    private readonly StringBuilder _current = new();
    private bool _failed;

    /// <summary>Raised once per completed rebuild.</summary>
    public event Action<BuildResult>? BuildCompleted;

    /// <summary>Raised when a rebuild starts, so the client can show a pending state.</summary>
    public event Action? BuildStarted;

    /// <summary>Raised for each stderr line, so the terminal sees esbuild verbatim.</summary>
    public event Action<string>? Line;

    public async Task<BuildResult> StartAsync(string outDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outDir);

        var bundler = new EsbuildBundler(esbuildPath, project, vendor);
        var args = bundler.BuildArguments(outDir);
        args.Add("--watch");

        var psi = new ProcessStartInfo(esbuildPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            // esbuild --watch exits as soon as stdin closes. Holding the pipe open keeps it
            // running, and means that if this process dies the pipe breaks and esbuild
            // shuts itself down -- no orphaned watchers left behind.
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = project.Root,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start esbuild at {esbuildPath}");

        // esbuild reports everything -- progress and diagnostics alike -- on stderr.
        var first = new TaskCompletionSource<BuildResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(() => PumpAsync(_process.StandardError, first, ct), ct);
        _ = Task.Run(async () =>
        {
            // Drain stdout so a full pipe buffer can never block the child.
            try { await _process.StandardOutput.ReadToEndAsync(ct); } catch { /* shutting down */ }
        }, ct);

        using var reg = ct.Register(() => first.TrySetCanceled(ct));
        return await first.Task;
    }

    /// <summary>
    /// State machine over esbuild's watch output:
    ///   "[watch] build started" -> reset  |  "✘ [ERROR]" -> mark failed
    ///   "[watch] build finished" -> emit
    /// </summary>
    private async Task PumpAsync(StreamReader stderr, TaskCompletionSource<BuildResult> first, CancellationToken ct)
    {
        try
        {
            while (await stderr.ReadLineAsync(ct) is { } line)
            {
                Line?.Invoke(line);

                if (line.Contains("[watch] build started", StringComparison.Ordinal))
                {
                    _current.Clear();
                    _failed = false;
                    BuildStarted?.Invoke();
                    continue;
                }

                if (line.Contains("[watch] build finished", StringComparison.Ordinal))
                {
                    var result = new BuildResult(!_failed, _current.ToString().Trim());
                    first.TrySetResult(result);
                    BuildCompleted?.Invoke(result);
                    continue;
                }

                if (line.Contains("[ERROR]", StringComparison.Ordinal)) _failed = true;
                _current.AppendLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            first.TrySetException(ex);
        }

        // esbuild exited without ever finishing a build: surface whatever it said.
        first.TrySetResult(new BuildResult(false,
            _current.Length > 0 ? _current.ToString().Trim() : "esbuild exited unexpectedly."));
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null || _process.HasExited) return;
        try
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        catch
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
