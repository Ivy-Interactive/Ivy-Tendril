using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Build;
using Ivy.Tendril.Wireframe.Hosting;
using Ivy.Tendril.Wireframe.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands.Wireframe;

public sealed class WireframeServeSettings : WireframeProjectSettings
{
    [CommandOption("--open")]
    [Description("Open the wireframe in the default browser.")]
    public bool Open { get; init; }

    [CommandOption("--port <PORT>")]
    [Description("Bind a specific port instead of a free one.")]
    public int Port { get; init; }

    [CommandOption("--print-url")]
    [Description("Print only the URL, for scripting.")]
    public bool PrintUrl { get; init; }
}

public sealed class WireframeServeCommand : AsyncCommand<WireframeServeSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, WireframeServeSettings settings, CancellationToken cancellation)
    {
        var assets = AssetCatalog.Default;
        var project = WireframeProject.At(settings.Path);

        if (!project.Exists)
        {
            AnsiConsole.MarkupLine(
                $"[red]No wireframe project at[/] [blue]{project.Root.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"  Run [blue]tendril wireframe setup {settings.Path.EscapeMarkup()}[/] first.");
            return 1;
        }

        // A tool upgrade leaves .wireframe/ holding the previous payload's types.
        var scaffolder = new ProjectScaffolder(assets);
        if (scaffolder.NeedsRefresh(project)) scaffolder.MaterializeWorkspace(project);

        var esbuild = await new EsbuildProvisioner().ResolveAsync(cancellation);
        var vendor = VendorManifest.Load(assets);
        var outDir = project.OutDir("serve");
        var linter = WireframeConfig.Load(project).Tailwind == TailwindMode.Jit
            ? null
            : new UtilityClassLinter(CssSelectorIndex.FromAssets(assets));

        // JIT mode replaces the embedded utility sheet with one Tailwind generates from
        // this project's own source, watched alongside the bundle.
        var config = WireframeConfig.Load(project);
        IAsyncDisposable? tailwind = null;
        string? utilityCss = null;

        if (config.Tailwind == TailwindMode.Jit)
        {
            var binary = await TailwindSupport.ProvisionAsync(settings.Quiet, cancellation);
            if (binary is null) return 1;

            var compiler = new TailwindCompiler(binary, assets, project);
            tailwind = await compiler.StartWatchAsync(cancellation);
            utilityCss = TailwindCompiler.OutputPath(project);
        }

        await using var watcher = new EsbuildWatcher(esbuild, project, vendor);
        await using var server = new WireframeServer(assets,
            new ServerOptions(project, outDir, LiveReload: true, settings.Port)
            {
                UtilityCssPath = utilityCss,
            });

        var quiet = settings.Quiet || settings.PrintUrl;

        // The first build is reported by the startup banner; announcing it as a "rebuild"
        // before the banner has even printed reads as noise.
        var isFirstBuild = true;

        watcher.BuildCompleted += result =>
        {
            var initial = isFirstBuild;
            isFirstBuild = false;

            if (result.Success)
            {
                if (!initial) server.Hub.Broadcast(new { type = "reload" });
                if (!quiet && !initial)
                    AnsiConsole.MarkupLine($"  [green]rebuilt[/] [grey]{DateTime.Now:HH:mm:ss}[/]");

                // A class with no rule behind it is invisible in the browser and in the
                // build output, so it has to be called out explicitly. Skipped under JIT,
                // where Tailwind generates whatever the source asks for.
                if (!quiet && linter is not null) ReportClassWarnings(linter.Lint(project));
            }
            else
            {
                server.Hub.Broadcast(new
                {
                    type = "error",
                    text = result.Output,
                    location = result.FirstLocation,
                });
                // esbuild's own diagnostics are better than anything we would render, so
                // pass them through verbatim. The initial build's errors are printed once
                // by the startup path below instead.
                if (!initial)
                {
                    AnsiConsole.WriteLine();
                    System.Console.Error.WriteLine(result.Output);
                }
            }
        };

        server.PageError += (kind, detail) =>
        {
            AnsiConsole.MarkupLine($"  [red]{kind.EscapeMarkup()}[/] [grey]in the browser[/]");
            System.Console.Error.WriteLine(detail);
        };

        var first = await watcher.StartAsync(outDir, cancellation);
        await server.StartAsync(cancellation);

        if (settings.PrintUrl)
        {
            System.Console.Out.WriteLine(server.Url);
        }
        else if (!settings.Quiet)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"  [green]wireframe[/]  [link={server.Url}]{server.Url}[/]");
            AnsiConsole.MarkupLine($"  [grey]watching   {project.RelativePath(project.SourceDir).EscapeMarkup()}/[/]");
            AnsiConsole.MarkupLine("  [grey]ctrl+c to stop[/]");
            AnsiConsole.WriteLine();

            if (!first.Success)
            {
                System.Console.Error.WriteLine(first.Output);
            }
        }

        if (settings.Open) TryOpenBrowser(server.Url);

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C. Disposal below stops esbuild and the server.
        }

        if (tailwind is not null) await tailwind.DisposeAsync();

        if (!quiet)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("  [grey]stopped[/]");
        }
        return 0;
    }

    /// <summary>Prints classes that produce no CSS, capped so one bad edit cannot bury
    /// the terminal.</summary>
    private static void ReportClassWarnings(IReadOnlyList<ClassWarning> warnings)
    {
        const int Max = 8;
        foreach (var warning in warnings.Take(Max))
            AnsiConsole.MarkupLine($"  [yellow]![/] [grey]{warning.Message.EscapeMarkup()}[/]");

        if (warnings.Count > Max)
            AnsiConsole.MarkupLine($"  [yellow]![/] [grey]...and {warnings.Count - Max} more[/]");
    }

    /// <summary>
    /// Opening a browser must never take the server down with it: headless CI and WSL
    /// both fail here routinely, and the URL is still perfectly usable.
    /// </summary>
    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                var browser = Environment.GetEnvironmentVariable("BROWSER");
                var candidates = string.IsNullOrWhiteSpace(browser)
                    ? new[] { "xdg-open", "gio", "sensible-browser", "x-www-browser" }
                    : [browser];

                foreach (var exe in candidates)
                {
                    try
                    {
                        var args = exe == "gio" ? $"open {url}" : url;
                        Process.Start(exe, args);
                        return;
                    }
                    catch (Exception e) when (e is System.ComponentModel.Win32Exception or FileNotFoundException)
                    {
                        // Try the next launcher.
                    }
                }
                AnsiConsole.MarkupLine("  [yellow]![/] [grey]could not open a browser; use the URL above[/]");
            }
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine($"  [yellow]![/] [grey]could not open a browser ({e.Message.EscapeMarkup()})[/]");
        }
    }
}
