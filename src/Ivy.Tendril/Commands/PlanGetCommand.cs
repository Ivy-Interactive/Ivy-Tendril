using System.ComponentModel;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanGetSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Optional field name to read")]
    [CommandArgument(1, "[field]")]
    public string? Field { get; set; }
}

public class PlanGetCommand : Command<PlanGetSettings>
{
    public override int Execute(CommandContext context, PlanGetSettings settings)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (string.IsNullOrWhiteSpace(settings.Field))
            {
                // Output full YAML
                var yaml = YamlHelper.Serializer.Serialize(plan);
                Console.Write(yaml);
            }
            else
            {
                // Output single field
                var value = settings.Field.ToLower() switch
                {
                    "state" => plan.State,
                    "project" => plan.Project,
                    "level" => plan.Level,
                    "title" => plan.Title,
                    "created" => plan.Created.ToString("O"),
                    "updated" => plan.Updated.ToString("O"),
                    "executionprofile" => plan.ExecutionProfile ?? "",
                    "initialprompt" => plan.InitialPrompt ?? "",
                    "sourceurl" => plan.SourceUrl ?? "",
                    "priority" => plan.Priority.ToString(),
                    _ => throw new ArgumentException($"Unknown field: {settings.Field}")
                };

                Console.WriteLine(value);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
