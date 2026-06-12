using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanGetSettings : CommandSettings
{
    internal static readonly string[] ValidFields =
        ["state", "project", "level", "title", "created", "updated", "executionprofile", "initialprompt", "sourceurl", "sourceidentifier", "priority",
         "repos", "prs", "commits", "verifications", "dependson", "relatedplans", "recommendations"];

    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Optional field name to read (state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, sourceIdentifier, priority, repos, prs, commits, verifications, dependsOn, relatedPlans, recommendations)")]
    [CommandArgument(1, "[field]")]
    public string? Field { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var result = CliValidation.RequireNonEmpty(PlanId, "plan-id");
        if (!result.Successful) return result;

        if (!string.IsNullOrWhiteSpace(Field))
        {
            if (!ValidFields.Contains(Field.ToLower(), StringComparer.OrdinalIgnoreCase))
                return Spectre.Console.ValidationResult.Error(
                    $"Unknown field '{Field}'. Valid fields: state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, sourceIdentifier, priority, repos, prs, commits, verifications, dependsOn, relatedPlans, recommendations");
        }

        return Spectre.Console.ValidationResult.Success();
    }
}

public class PlanGetCommand : Command<PlanGetSettings>
{
    protected override int Execute(CommandContext context, PlanGetSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        if (string.IsNullOrWhiteSpace(settings.Field))
        {
            var yaml = YamlHelper.Serializer.Serialize(plan);
            Console.Write(yaml);
        }
        else
        {
            var field = settings.Field.ToLower();

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
                "sourceidentifier" => plan.SourceIdentifier ?? "",
                "priority" => plan.Priority.ToString(),
                _ => throw new ArgumentException($"Unknown field '{settings.Field}'. Valid fields: state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, sourceIdentifier, priority, repos, prs, commits, verifications, dependsOn, relatedPlans, recommendations")
            };

            Console.WriteLine(value);
        }

        return 0;
    }
}
