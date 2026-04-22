using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanSetSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Field name (state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, priority)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";
}

public class PlanSetCommand : Command<PlanSetSettings>
{
    protected override int Execute(CommandContext context, PlanSetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Update the field
            switch (settings.Field.ToLower())
            {
                case "state":
                    plan.State = settings.Value;
                    break;
                case "project":
                    plan.Project = settings.Value;
                    break;
                case "level":
                    plan.Level = settings.Value;
                    break;
                case "title":
                    plan.Title = settings.Value;
                    break;
                case "created":
                    plan.Created = PlanValidationService.ParseDate(settings.Value, "created");
                    break;
                case "updated":
                    plan.Updated = PlanValidationService.ParseDate(settings.Value, "updated");
                    break;
                case "executionprofile":
                    plan.ExecutionProfile = settings.Value;
                    break;
                case "initialprompt":
                    plan.InitialPrompt = settings.Value;
                    break;
                case "sourceurl":
                    plan.SourceUrl = settings.Value;
                    break;
                case "priority":
                    if (!int.TryParse(settings.Value, out var priority))
                        throw new ArgumentException($"Invalid priority value: {settings.Value}. Must be an integer.");
                    plan.Priority = priority;
                    break;
                default:
                    throw new ArgumentException($"Unknown field: {settings.Field}");
            }

            // Always update the 'updated' timestamp when any field changes
            if (settings.Field.ToLower() != "updated")
                plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Updated {settings.Field} to '{settings.Value}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
