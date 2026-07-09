using System.ComponentModel;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanUpdateSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [CommandOption("-f|--file")]
    [Description("Read the YAML content from this file")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read the YAML content from standard input")]
    public bool Stdin { get; set; }

    public int SourceCount => CliValidation.CountSources(Stdin, FilePath, "");

    public override Spectre.Console.ValidationResult Validate()
    {
        var sourceValidation = CliValidation.ValidateSingleSource(SourceCount, "--file or --stdin");
        if (!sourceValidation.Successful)
            return sourceValidation;

        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanUpdateCommand : Command<PlanUpdateSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanUpdateCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanUpdateSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        var yaml = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("No YAML content provided (use --file or --stdin)");

        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
        if (plan == null)
            throw new InvalidOperationException("Failed to deserialize YAML from STDIN");

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Updated plan {settings.PlanId}");
        return 0;
    }
}
