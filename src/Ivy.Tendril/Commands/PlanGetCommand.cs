using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
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
    protected override int Execute(CommandContext context, PlanGetSettings settings, CancellationToken cancellationToken)
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
                var field = settings.Field.ToLower();

                // List fields — output one item per line
                switch (field)
                {
                    case "repos":
                        foreach (var r in plan.Repos) Console.WriteLine(r);
                        return 0;
                    case "prs":
                        foreach (var pr in plan.Prs) Console.WriteLine(pr);
                        return 0;
                    case "commits":
                        foreach (var c in plan.Commits) Console.WriteLine(c);
                        return 0;
                    case "verifications":
                        foreach (var v in plan.Verifications) Console.WriteLine($"{v.Name}={v.Status}");
                        return 0;
                    case "dependson":
                        foreach (var d in plan.DependsOn) Console.WriteLine(d);
                        return 0;
                    case "relatedplans":
                        foreach (var rp in plan.RelatedPlans) Console.WriteLine(rp);
                        return 0;
                    case "recommendations":
                        foreach (var rec in plan.Recommendations ?? [])
                            Console.WriteLine($"{rec.Title}={rec.State}");
                        return 0;
                }

                // Scalar fields
                var value = field switch
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
