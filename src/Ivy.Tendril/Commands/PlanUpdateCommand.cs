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

        var yaml = ConsoleHelper.ReadStdinWithTimeout();
        if (string.IsNullOrWhiteSpace(yaml))
            throw new ArgumentException("No YAML content provided on STDIN");

        var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
        if (plan == null)
            throw new InvalidOperationException("Failed to deserialize YAML from STDIN");

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Updated plan {settings.PlanId}");
        return 0;
    }
}
