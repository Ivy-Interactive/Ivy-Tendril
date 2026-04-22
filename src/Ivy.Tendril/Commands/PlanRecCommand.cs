using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRecListSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [CommandOption("--state")]
    [Description("Filter by state (Pending, Accepted, Declined)")]
    public string? State { get; set; }
}

public class PlanRecAddSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";

    [CommandOption("-d|--description")]
    [Description("Recommendation description (reads from stdin if omitted)")]
    public string? Description { get; set; }

    [CommandOption("--impact")]
    [Description("Impact level (Small, Medium, High)")]
    public string? Impact { get; set; }

    [CommandOption("--risk")]
    [Description("Risk level (Small, Medium, High)")]
    public string? Risk { get; set; }
}

public class PlanRecRemoveSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";
}

public class PlanRecAcceptSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";

    [CommandOption("--notes")]
    [Description("Optional notes to include")]
    public string? Notes { get; set; }
}

public class PlanRecDeclineSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";

    [CommandOption("--reason")]
    [Description("Decline reason")]
    public string? Reason { get; set; }
}

public class PlanRecSetSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";

    [Description("Field name (title, description, state, impact, risk, declineReason)")]
    [CommandArgument(2, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(3, "<value>")]
    public string Value { get; set; } = "";
}

public class PlanRecListCommand : Command<PlanRecListSettings>
{
    protected override int Execute(CommandContext context, PlanRecListSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            var recs = plan.Recommendations ?? [];

            if (!string.IsNullOrEmpty(settings.State))
                recs = recs.Where(r => r.State.Equals(settings.State, StringComparison.OrdinalIgnoreCase)).ToList();

            if (recs.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]No recommendations found.[/]");
                return 0;
            }

            var table = new Spectre.Console.Table();
            table.AddColumn("Title");
            table.AddColumn("State");
            table.AddColumn("Impact");
            table.AddColumn("Risk");

            foreach (var rec in recs)
                table.AddRow(
                    rec.Title.EscapeMarkup(),
                    rec.State.EscapeMarkup(),
                    (rec.Impact ?? "-").EscapeMarkup(),
                    (rec.Risk ?? "-").EscapeMarkup());

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PlanRecAddCommand : Command<PlanRecAddSettings>
{
    protected override int Execute(CommandContext context, PlanRecAddSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            plan.Recommendations ??= [];

            if (plan.Recommendations.Any(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"Error: Recommendation '{settings.Title}' already exists");
                return 1;
            }

            var description = settings.Description;
            if (string.IsNullOrEmpty(description))
            {
                if (!Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Error: Provide --description or pipe content via stdin");
                    return 1;
                }
                description = Console.In.ReadToEnd().Trim();
            }

            plan.Recommendations.Add(new RecommendationYaml
            {
                Title = settings.Title,
                Description = description,
                State = "Pending",
                Impact = settings.Impact,
                Risk = settings.Risk
            });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Added recommendation '{settings.Title}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PlanRecRemoveCommand : Command<PlanRecRemoveSettings>
{
    protected override int Execute(CommandContext context, PlanRecRemoveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var recs = plan.Recommendations ?? [];
            var match = recs.FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                Console.Error.WriteLine($"Error: Recommendation '{settings.Title}' not found");
                return 1;
            }

            recs.Remove(match);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Removed recommendation '{settings.Title}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PlanRecAcceptCommand : Command<PlanRecAcceptSettings>
{
    protected override int Execute(CommandContext context, PlanRecAcceptSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

            if (rec == null)
            {
                Console.Error.WriteLine($"Error: Recommendation '{settings.Title}' not found");
                return 1;
            }

            rec.State = string.IsNullOrEmpty(settings.Notes) ? "Accepted" : "AcceptedWithNotes";
            rec.DeclineReason = null;

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Accepted recommendation '{settings.Title}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PlanRecDeclineCommand : Command<PlanRecDeclineSettings>
{
    protected override int Execute(CommandContext context, PlanRecDeclineSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

            if (rec == null)
            {
                Console.Error.WriteLine($"Error: Recommendation '{settings.Title}' not found");
                return 1;
            }

            rec.State = "Declined";
            rec.DeclineReason = settings.Reason;

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan);

            Console.WriteLine($"Declined recommendation '{settings.Title}'");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

public class PlanRecSetCommand : Command<PlanRecSetSettings>
{
    protected override int Execute(CommandContext context, PlanRecSetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var rec = (plan.Recommendations ?? [])
                .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

            if (rec == null)
            {
                Console.Error.WriteLine($"Error: Recommendation '{settings.Title}' not found");
                return 1;
            }

            switch (settings.Field.ToLower())
            {
                case "title":
                    rec.Title = settings.Value;
                    break;
                case "description":
                    rec.Description = settings.Value;
                    break;
                case "state":
                    rec.State = settings.Value;
                    break;
                case "impact":
                    rec.Impact = settings.Value;
                    break;
                case "risk":
                    rec.Risk = settings.Value;
                    break;
                case "declinereason":
                    rec.DeclineReason = settings.Value;
                    break;
                default:
                    throw new ArgumentException($"Unknown field: {settings.Field}");
            }

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
