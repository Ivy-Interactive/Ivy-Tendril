using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
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
    protected override int Execute(CommandContext context, PlanUpdateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

            // Read YAML from STDIN
            var yaml = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(yaml))
                throw new ArgumentException("No YAML content provided on STDIN");

            // Deserialize
            var plan = YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
            if (plan == null)
                throw new InvalidOperationException("Failed to deserialize YAML from STDIN");

            // Write with validation
            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Updated plan {settings.PlanId}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
