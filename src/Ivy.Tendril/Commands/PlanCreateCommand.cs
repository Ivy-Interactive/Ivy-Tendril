using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanCreateSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Plan title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";
}

public class PlanCreateCommand : Command<PlanCreateSettings>
{
    protected override int Execute(CommandContext context, PlanCreateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

            // Check if plan.yaml already exists
            var yamlPath = Path.Combine(planFolder, "plan.yaml");
            if (File.Exists(yamlPath))
            {
                Console.Error.WriteLine($"Error: plan.yaml already exists at {yamlPath}");
                return 1;
            }

            // Create new plan with minimal required fields
            var plan = new PlanYaml
            {
                State = "Draft",
                Project = "Auto",
                Level = "NiceToHave",
                Title = settings.Title,
                Created = DateTime.UtcNow,
                Updated = DateTime.UtcNow
            };

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Created plan {settings.PlanId}: {settings.Title}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
