using System.ComponentModel;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Project;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands.Wireframe;

public sealed class WireframeSetupSettings : WireframeProjectSettings
{
    [CommandOption("--tailwind <MODE>")]
    [Description("'superset' (default, embedded, offline) or 'jit' (downloads the ~107 MB Tailwind standalone CLI for full fidelity).")]
    public string Tailwind { get; init; } = "superset";

    [CommandOption("--force")]
    [Description("Rewrite scaffold files that already exist.")]
    public bool Force { get; init; }
}

public sealed class WireframeSetupCommand : AsyncCommand<WireframeSetupSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, WireframeSetupSettings settings, CancellationToken cancellation)
    {
        if (Ivy.Tendril.Services.Wireframes.PlanWireframes.DisabledReason(settings.Path) is { } disabled)
        {
            AnsiConsole.MarkupLine($"[red]{disabled.EscapeMarkup()}[/]");
            return 1;
        }

        if (!WireframeConfig.TryParseTailwind(settings.Tailwind, out var tailwind))
        {
            AnsiConsole.MarkupLine(
                $"[red]Unknown --tailwind mode[/] '{settings.Tailwind.EscapeMarkup()}'. Use 'superset' or 'jit'.");
            return 1;
        }

        var assets = AssetCatalog.Default;
        var project = WireframeProject.At(settings.Path);

        if (settings.Force && Directory.Exists(project.SourceDir))
        {
            foreach (var f in new[] { "main.tsx", "App.tsx", "wireframe-ready.ts" })
            {
                var p = System.IO.Path.Combine(project.SourceDir, f);
                if (File.Exists(p)) File.Delete(p);
            }
            if (File.Exists(project.IndexHtml)) File.Delete(project.IndexHtml);
            if (File.Exists(project.TsConfig)) File.Delete(project.TsConfig);
        }

        var scaffolder = new ProjectScaffolder(assets);
        var result = scaffolder.Scaffold(project);
        var manifest = VendorManifest.Load(assets);

        new WireframeConfig { Tailwind = tailwind }.Save(project);

        // Pull the binary down now rather than at the first `serve`, so the cost lands on
        // the command the user explicitly opted into it with.
        if (tailwind == TailwindMode.Jit
            && await TailwindSupport.ProvisionAsync(settings.Quiet, cancellation) is null)
        {
            return 1;
        }

        if (settings.Quiet) return 0;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [green]wireframe[/] project ready at [blue]{project.Root.EscapeMarkup()}[/]");
        AnsiConsole.WriteLine();

        foreach (var f in result.Created)
            AnsiConsole.MarkupLine($"    [green]+[/] {f.EscapeMarkup()}");
        foreach (var f in result.Skipped)
            AnsiConsole.MarkupLine($"    [grey]. {f.EscapeMarkup()} (exists, kept)[/]");

        AnsiConsole.MarkupLine($"    [grey]. .wireframe/ ({result.TypeFiles} type files)[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"  [grey]tendril-wireframes {manifest.TendrilVersion}  ·  react {manifest.ReactVersion}  ·  no node required[/]");

        if (tailwind == TailwindMode.Jit)
            AnsiConsole.MarkupLine("  [grey]tailwind: standalone CLI (full fidelity, incl. arbitrary values)[/]");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("  next:");
        AnsiConsole.MarkupLine($"    [blue]tendril wireframe serve {Rel(project.Root).EscapeMarkup()}[/]        live preview with hot reload");
        AnsiConsole.MarkupLine($"    [blue]tendril wireframe screenshot {Rel(project.Root).EscapeMarkup()}[/]   render to screenshots/");
        AnsiConsole.MarkupLine("    [blue]tendril wireframe agent-readme[/]          component and prop reference");
        AnsiConsole.WriteLine();

        return 0;
    }

    private static string Rel(string root)
    {
        var rel = System.IO.Path.GetRelativePath(Directory.GetCurrentDirectory(), root);
        return rel == "." ? "." : rel.Replace('\\', '/');
    }
}
