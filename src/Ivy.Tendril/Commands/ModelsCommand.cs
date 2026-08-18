using System.ComponentModel;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public sealed class ModelsCommandSettings : CommandSettings
{
    [CommandOption("-a|--agent")]
    [Description("Filter to a specific agent (e.g. claude, codex, copilot)")]
    public string? Agent { get; set; }
}

public sealed class ModelsCommand(IAgentRunner runner) : AsyncCommand<ModelsCommandSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context, ModelsCommandSettings settings, CancellationToken cancellationToken)
    {
        var agentIds = !string.IsNullOrWhiteSpace(settings.Agent)
            ? [settings.Agent]
            : runner.RegisteredAgents;

        if (!string.IsNullOrWhiteSpace(settings.Agent) && runner.GetModelCatalog(settings.Agent) is null)
        {
            AnsiConsole.MarkupLine($"[red]Unknown agent:[/] {settings.Agent.EscapeMarkup()}");
            AnsiConsole.MarkupLine($"[dim]Available agents:[/] {string.Join(", ", runner.RegisteredAgents)}");
            return 1;
        }

        foreach (var agentId in agentIds)
        {
            var healthCheck = runner.GetHealthCheck(agentId);
            var descriptor = runner.GetDescriptor(agentId);
            var catalog = runner.GetModelCatalog(agentId);

            var installStatus = await healthCheck.CheckInstallAsync(cancellationToken);
            var authResult = await healthCheck.CheckAuthAsync(cancellationToken);

            var installed = installStatus.IsInstalled;
            var authenticated = authResult.Status == AuthStatus.Authenticated;

            ModelCatalogResult? result = null;
            if (catalog is not null)
                result = await catalog.GetModelsAsync(cancellationToken);

            var sourceLabel = result is not null
                ? result.Source switch
                {
                    ModelCatalogSource.Dynamic => "[cyan]dynamic[/]",
                    ModelCatalogSource.Cached => "[cyan]dynamic (cached)[/]",
                    ModelCatalogSource.Fallback => "[yellow]static (fallback)[/]",
                    _ => "[dim]static[/]",
                }
                : "[dim]none[/]";

            AnsiConsole.MarkupLine($"[bold]{agentId.EscapeMarkup()}[/]");
            AnsiConsole.MarkupLine(
                $"  Installed: {(installed ? "[green]YES[/]" : "[red]NO[/]")}  " +
                $"Authenticated: {(authenticated ? "[green]YES[/]" : "[red]NO[/]")}  " +
                $"Models: {sourceLabel}");

            if (descriptor.DefaultProfiles.Count > 0)
            {
                AnsiConsole.MarkupLine("  [dim]Profiles:[/]");
                foreach (var profile in descriptor.DefaultProfiles)
                {
                    var model = profile.Model ?? "-";
                    AnsiConsole.MarkupLine($"    {profile.Name.EscapeMarkup(),-10} : {model.EscapeMarkup()}");
                }
            }

            if (result is { Models.Count: > 0 })
                RenderTable(result);

            AnsiConsole.WriteLine();
        }

        return 0;
    }

    private static void RenderTable(ModelCatalogResult result)
    {
        var headers = new[]
        {
            "Model", "Display Name", "Input $/M", "Output $/M", "Cache R $/M", "Cache W $/M",
            "Source", "Default", "Vision"
        };

        var sourceIndex = new Dictionary<string, int>();
        var rows = new List<IReadOnlyList<string>>();

        foreach (var model in result.Models)
        {
            var isDefault = model.IsDefault ? "*" : "";
            var sourceRef = "";

            if (model.PricingSource is not null)
            {
                if (!sourceIndex.TryGetValue(model.PricingSource, out var idx))
                {
                    idx = sourceIndex.Count + 1;
                    sourceIndex[model.PricingSource] = idx;
                }
                sourceRef = idx.ToString();
            }

            var hasVision = model.Capabilities.HasFlag(ModelCapabilities.ImageInput) ? CliOutput.Glyph(true) : "-";

            rows.Add(new[]
            {
                model.Id,
                model.DisplayName,
                FormatPrice(model.InputPerMillion),
                FormatPrice(model.OutputPerMillion),
                FormatPrice(model.CacheReadPerMillion),
                FormatPrice(model.CacheWritePerMillion),
                sourceRef,
                isDefault,
                hasVision
            });
        }

        CliOutput.WriteTable(headers, rows, TableBorder.Rounded);

        if (sourceIndex.Count > 0)
        {
            foreach (var (source, idx) in sourceIndex.OrderBy(x => x.Value))
                AnsiConsole.MarkupLine($"  [dim]{idx}) {source.EscapeMarkup()}[/]");
        }
    }

    private static string FormatPrice(decimal price) =>
        price == 0m ? "[dim]-[/]" : FormatHelper.FormatCost(price);
}
