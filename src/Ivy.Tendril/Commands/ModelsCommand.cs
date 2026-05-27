using System.ComponentModel;
using Ivy.Tendril.Agents.Abstractions;
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

            AnsiConsole.MarkupLine($"[bold]{agentId}[/]");
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
                    AnsiConsole.MarkupLine($"    {profile.Name,-10} : {model}");
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
        var table = new Spectre.Console.Table();
        table.Border(TableBorder.Rounded);

        table.AddColumn("Model");
        table.AddColumn("Display Name");
        table.AddColumn(new TableColumn("Input $/M").RightAligned());
        table.AddColumn(new TableColumn("Output $/M").RightAligned());
        table.AddColumn(new TableColumn("Cache R $/M").RightAligned());
        table.AddColumn(new TableColumn("Cache W $/M").RightAligned());
        table.AddColumn("Source");
        table.AddColumn("Default");

        var sourceIndex = new Dictionary<string, int>();

        foreach (var model in result.Models)
        {
            var isDefault = model.IsDefault ? "[green]*[/]" : "";
            var sourceRef = "";

            if (model.PricingSource is not null)
            {
                if (!sourceIndex.TryGetValue(model.PricingSource, out var idx))
                {
                    idx = sourceIndex.Count + 1;
                    sourceIndex[model.PricingSource] = idx;
                }
                sourceRef = $"[dim]{idx}[/]";
            }

            table.AddRow(
                model.Id.EscapeMarkup(),
                model.DisplayName.EscapeMarkup(),
                FormatPrice(model.InputPerMillion),
                FormatPrice(model.OutputPerMillion),
                FormatPrice(model.CacheReadPerMillion),
                FormatPrice(model.CacheWritePerMillion),
                sourceRef,
                isDefault);
        }

        AnsiConsole.Write(table);

        if (sourceIndex.Count > 0)
        {
            foreach (var (source, idx) in sourceIndex.OrderBy(x => x.Value))
                AnsiConsole.MarkupLine($"  [dim]{idx}) {source.EscapeMarkup()}[/]");
        }
    }

    private static string FormatPrice(decimal price) =>
        price == 0m ? "[dim]-[/]" : $"${price:F2}";
}
