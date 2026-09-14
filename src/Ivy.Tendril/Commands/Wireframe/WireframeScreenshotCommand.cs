using System.ComponentModel;
using System.Diagnostics;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Build;
using Ivy.Tendril.Wireframe.Hosting;
using Ivy.Tendril.Wireframe.Project;
using Ivy.Tendril.Wireframe.Screenshot;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands.Wireframe;

public sealed class WireframeScreenshotSettings : WireframeProjectSettings
{
    [CommandOption("-w|--width <PX>")]
    [Description("Viewport width in CSS pixels. Default 1440.")]
    public int Width { get; init; } = 1440;

    // Long form only: Spectre reserves -h for --help, and shadowing it would make
    // `tendril wireframe screenshot -h` stop printing help, which reads as a bug.
    [CommandOption("--height <PX>")]
    [Description("Viewport height in CSS pixels. Default 900.")]
    public int Height { get; init; } = 900;

    [CommandOption("-s|--scale <FACTOR>")]
    [Description("Device pixel ratio. Default 2, because 1.4px hand-drawn strokes alias badly at 1x.")]
    public double Scale { get; init; } = 2;

    [CommandOption("--full")]
    [Description("Capture the full page height instead of just the viewport.")]
    public bool Full { get; init; }

    [CommandOption("--transparent")]
    [Description("Leave the page background transparent.")]
    public bool Transparent { get; init; }

    [CommandOption("--url <URL>")]
    [Description("Capture an already-running URL instead of building and serving.")]
    public string? Url { get; init; }

    [CommandOption("--browser <PATH>")]
    [Description("Path to a Chrome/Edge/Chromium executable.")]
    public string? Browser { get; init; }

    [CommandOption("--timeout <SECONDS>")]
    [Description("How long to wait for the wireframe to finish drawing. Default 30.")]
    public int TimeoutSeconds { get; init; } = 30;

    [CommandOption("--repeat <N>")]
    [Description("Capture N times and verify every PNG is byte-identical (determinism check).")]
    public int Repeat { get; init; } = 1;

    public override Spectre.Console.ValidationResult Validate()
    {
        if (Width is < 64 or > 8192)
            return Spectre.Console.ValidationResult.Error("--width must be between 64 and 8192.");
        if (Height is < 64 or > 16384)
            return Spectre.Console.ValidationResult.Error("--height must be between 64 and 16384.");
        if (Scale is < 0.25 or > 4)
            return Spectre.Console.ValidationResult.Error("--scale must be between 0.25 and 4.");
        return Spectre.Console.ValidationResult.Success();
    }
}

public sealed class WireframeScreenshotCommand : AsyncCommand<WireframeScreenshotSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, WireframeScreenshotSettings settings, CancellationToken cancellation)
    {
        var assets = AssetCatalog.Default;
        var project = WireframeProject.At(settings.Path);

        if (settings.Url is null && !project.Exists)
        {
            AnsiConsole.MarkupLine($"[red]No wireframe project at[/] [blue]{project.Root.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine($"  Run [blue]tendril wireframe setup {settings.Path.EscapeMarkup()}[/] first.");
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();
        WireframeServer? server = null;

        try
        {
            var url = settings.Url;

            if (url is null)
            {
                // Always build and serve fresh rather than reusing a running `serve`.
                // Discovery would need a pid/port lockfile with stale-entry and PID-reuse
                // handling, and every bug in that lands as "screenshotted the wrong app
                // into the right filename". A live serve is also the wrong environment on
                // purpose: reload client attached, possibly an error overlay open, and an
                // esbuild --watch that may be mid-rebuild.
                var scaffolder = new ProjectScaffolder(assets);
                if (scaffolder.NeedsRefresh(project)) scaffolder.MaterializeWorkspace(project);

                var esbuild = await new EsbuildProvisioner().ResolveAsync(cancellation);
                var vendor = VendorManifest.Load(assets);
                var outDir = project.OutDir("screenshot");

                var build = await new EsbuildBundler(esbuild, project, vendor)
                    .BuildOnceAsync(outDir, cancellation);

                if (!build.Success)
                {
                    AnsiConsole.MarkupLine("[red]build failed[/]");
                    System.Console.Error.WriteLine(build.Output);
                    return 1;
                }

                var config = WireframeConfig.Load(project);
                string? utilityCss = null;

                if (config.Tailwind == TailwindMode.Jit)
                {
                    var binary = await TailwindSupport.ProvisionAsync(settings.Quiet, cancellation);
                    if (binary is null) return 1;

                    var css = await new TailwindCompiler(binary, assets, project)
                        .CompileOnceAsync(cancellation);

                    if (!css.Success)
                    {
                        AnsiConsole.MarkupLine("[red]Tailwind failed[/]");
                        System.Console.Error.WriteLine(css.Output);
                        return 1;
                    }
                    utilityCss = TailwindCompiler.OutputPath(project);
                }
                else if (!settings.Quiet)
                {
                    // Worth surfacing here too: a screenshot of a page whose layout classes
                    // did nothing looks like a design mistake rather than a missing rule.
                    // Under JIT there is nothing to warn about.
                    var warnings = new UtilityClassLinter(CssSelectorIndex.FromAssets(assets)).Lint(project);
                    foreach (var warning in warnings.Take(8))
                        AnsiConsole.MarkupLine($"  [yellow]![/] [grey]{warning.Message.EscapeMarkup()}[/]");
                    if (warnings.Count > 8)
                        AnsiConsole.MarkupLine($"  [yellow]![/] [grey]...and {warnings.Count - 8} more[/]");
                }

                server = new WireframeServer(assets,
                    new ServerOptions(project, outDir, LiveReload: false) { UtilityCssPath = utilityCss });
                await server.StartAsync(cancellation);
                url = server.Url;
            }

            var output = OutputPath(project, settings);
            var runner = new ScreenshotRunner();
            var options = new ScreenshotOptions(
                url, output, settings.Width, settings.Height, settings.Scale,
                settings.Full, settings.Transparent,
                TimeSpan.FromSeconds(settings.TimeoutSeconds), settings.Browser);

            var result = await runner.CaptureAsync(options, cancellation);

            if (settings.Repeat > 1
                && await VerifyRepeatsAsync(runner, options, settings.Repeat, cancellation) is { } mismatch)
            {
                AnsiConsole.MarkupLine($"[red]determinism check failed:[/] {mismatch.EscapeMarkup()}");
                return 1;
            }

            if (!settings.Quiet)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(
                    $"  [green]captured[/] [blue]{project.RelativePath(result.Path).EscapeMarkup()}[/]");
                AnsiConsole.MarkupLine(
                    $"  [grey]{result.PixelWidth}x{result.PixelHeight}px · " +
                    $"{result.Bytes / 1024.0:F0} KB · {stopwatch.ElapsedMilliseconds} ms[/]");
                if (settings.Repeat > 1)
                    AnsiConsole.MarkupLine($"  [grey]{settings.Repeat} captures, all byte-identical[/]");
                if (result.Warning is not null)
                    AnsiConsole.MarkupLine($"  [yellow]![/] [grey]{result.Warning.EscapeMarkup()}[/]");
                AnsiConsole.WriteLine();
            }
            else
            {
                System.Console.Out.WriteLine(result.Path);
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or FileNotFoundException)
        {
            AnsiConsole.MarkupLine("[red]screenshot failed[/]");
            AnsiConsole.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            if (server is not null) await server.DisposeAsync();
        }
    }

    /// <summary>
    /// screenshots/&lt;width&gt;x&lt;height&gt;.png, using CSS dimensions. A non-default
    /// scale gets an @Nx suffix so it cannot clobber the canonical shot.
    /// </summary>
    private static string OutputPath(WireframeProject project, WireframeScreenshotSettings settings)
    {
        var height = settings.Full ? "full" : settings.Height.ToString();
        var suffix = Math.Abs(settings.Scale - 2) < 0.001 ? "" : $"@{settings.Scale:0.##}x";
        return Path.Combine(project.ScreenshotsDir, $"{settings.Width}x{height}{suffix}.png");
    }

    /// <summary>Re-captures into a temp file and compares bytes. Returns null when stable.</summary>
    private static async Task<string?> VerifyRepeatsAsync(
        ScreenshotRunner runner, ScreenshotOptions options, int repeat, CancellationToken ct)
    {
        var reference = await File.ReadAllBytesAsync(options.OutputPath, ct);
        var temp = Path.Combine(Path.GetTempPath(), $"wireframe-repeat-{Guid.NewGuid():N}.png");

        try
        {
            for (var i = 2; i <= repeat; i++)
            {
                var again = await runner.CaptureAsync(options with { OutputPath = temp }, ct);
                var bytes = await File.ReadAllBytesAsync(again.Path, ct);
                if (!reference.AsSpan().SequenceEqual(bytes))
                    return $"capture {i} of {repeat} differs from the first ({reference.Length} vs {bytes.Length} bytes)";
            }
            return null;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
