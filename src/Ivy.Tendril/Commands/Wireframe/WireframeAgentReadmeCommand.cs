using System.ComponentModel;
using Ivy.Tendril.Wireframe.Assets;
using Ivy.Tendril.Wireframe.Manifest;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands.Wireframe;

public sealed class WireframeAgentReadmeSettings : CommandSettings
{
    [CommandOption("--component <NAME>")]
    [Description("Print the full prop table for one component instead of the whole reference.")]
    public string? Component { get; init; }

    [CommandOption("-o|--out <FILE>")]
    [Description("Write to a file instead of stdout.")]
    public string? Out { get; init; }

    [CommandOption("--list")]
    [Description("Print just the component names, one per line.")]
    public bool List { get; init; }
}

public sealed class WireframeAgentReadmeCommand : Command<WireframeAgentReadmeSettings>
{
    protected override int Execute(
        CommandContext context, WireframeAgentReadmeSettings settings, CancellationToken cancellation)
    {
        var assets = AssetCatalog.Default;
        var manifest = ComponentManifest.Load(assets);
        var renderer = new AgentReadmeRenderer(manifest, VendorManifest.Load(assets));

        string text;

        if (settings.List)
        {
            text = string.Join(Environment.NewLine, manifest.PublicComponents.Select(c => c.Name));
        }
        else if (settings.Component is not null)
        {
            var component = manifest.Find(settings.Component);
            if (component is null)
            {
                AnsiConsole.MarkupLine($"[red]No component named[/] '{settings.Component.EscapeMarkup()}'.");

                // Cheap "did you mean": substring match.
                var suggestions = manifest.PublicComponents
                    .Where(c => c.Name.Contains(settings.Component, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Name)
                    .Take(8)
                    .ToList();

                if (suggestions.Count > 0)
                    AnsiConsole.MarkupLine($"  Did you mean: {string.Join(", ", suggestions).EscapeMarkup()}?");
                AnsiConsole.MarkupLine("  [grey]tendril wireframe agent-readme --list[/] shows every component.");
                return 1;
            }
            text = renderer.RenderComponent(component);
        }
        else
        {
            text = renderer.Render();
        }

        if (settings.Out is not null)
        {
            var full = System.IO.Path.GetFullPath(settings.Out);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
            AnsiConsole.MarkupLine($"  [green]wrote[/] [blue]{full.EscapeMarkup()}[/] " +
                                   $"[grey]({text.Length / 1024.0:F0} KB)[/]");
        }
        else
        {
            // Straight to stdout: this is piped into agents and files, so it must not
            // pick up Spectre's wrapping or markup interpretation.
            System.Console.Out.Write(text);
        }

        return 0;
    }
}
