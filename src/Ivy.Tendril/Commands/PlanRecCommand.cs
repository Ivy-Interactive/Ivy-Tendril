using System.ComponentModel;
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
    [Description("Filter by state (Pending, Accepted, AcceptedWithNotes, Declined)")]
    public string? State { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.ValidateOneOf(State, "--state", CliValidation.ValidRecommendationStates)
        );
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Title, "title"),
            CliValidation.ValidateOneOf(Impact, "--impact", CliValidation.ValidImpactLevels),
            CliValidation.ValidateOneOf(Risk, "--risk", CliValidation.ValidImpactLevels)
        );
    }
}

public class PlanRecRemoveSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Recommendation title")]
    [CommandArgument(1, "<title>")]
    public string Title { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Title, "title")
        );
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Title, "title")
        );
    }
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

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Title, "title")
        );
    }
}

public class PlanRecSetSettings : CommandSettings
{
    private static readonly string[] ValidFields = ["title", "description", "state", "impact", "risk", "declinereason"];

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

    public override Spectre.Console.ValidationResult Validate()
    {
        var required = CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Title, "title"),
            CliValidation.ValidateField(Field, ValidFields));
        if (!required.Successful)
            return required;

        var field = Field.ToLower();
        if (field == "state")
            return CliValidation.ValidateOneOf(Value, "<value> for field 'state'", CliValidation.ValidRecommendationStates);
        if (field == "impact" || field == "risk")
            return CliValidation.ValidateOneOf(Value, $"<value> for field '{field}'", CliValidation.ValidImpactLevels);

        return Spectre.Console.ValidationResult.Success();
    }
}

public class PlanRecListCommand : Command<PlanRecListSettings>
{
    protected override int Execute(CommandContext context, PlanRecListSettings settings, CancellationToken cancellationToken)
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
}

public class PlanRecAddCommand : Command<PlanRecAddSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRecAddCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRecAddSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        plan.Recommendations ??= [];

        if (plan.Recommendations.Any(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Recommendation already exists: {settings.Title}");

        var description = settings.Description;
        if (string.IsNullOrEmpty(description))
        {
            if (!Console.IsInputRedirected)
                throw new ArgumentException("Provide --description or pipe content via stdin");
            description = Console.In.ReadToEnd().Trim();
        }

        plan.Recommendations.Add(new RecommendationYaml
        {
            Title = settings.Title,
            Description = description,
            State = RecommendationStatus.Pending,
            Impact = settings.Impact,
            Risk = settings.Risk
        });

        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added recommendation: {settings.Title}");
        return 0;
    }
}

public class PlanRecRemoveCommand : Command<PlanRecRemoveSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRecRemoveCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRecRemoveSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var recs = plan.Recommendations ?? [];
        var match = recs.FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Recommendation not found: {settings.Title}");

        recs.Remove(match);
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Removed recommendation: {settings.Title}");
        return 0;
    }
}

public class PlanRecAcceptCommand : Command<PlanRecAcceptSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRecAcceptCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRecAcceptSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var rec = (plan.Recommendations ?? [])
            .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

        if (rec == null)
            throw new InvalidOperationException($"Recommendation not found: {settings.Title}");

        rec.State = string.IsNullOrEmpty(settings.Notes) ? RecommendationStatus.Accepted : RecommendationStatus.AcceptedWithNotes;
        rec.DeclineReason = null;

        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Accepted recommendation: {settings.Title}");
        return 0;
    }
}

public class PlanRecDeclineCommand : Command<PlanRecDeclineSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRecDeclineCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRecDeclineSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var rec = (plan.Recommendations ?? [])
            .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

        if (rec == null)
            throw new InvalidOperationException($"Recommendation not found: {settings.Title}");

        rec.State = RecommendationStatus.Declined;
        rec.DeclineReason = settings.Reason;

        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Declined recommendation: {settings.Title}");
        return 0;
    }
}

public class PlanRecSetCommand : Command<PlanRecSetSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanRecSetCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanRecSetSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var rec = (plan.Recommendations ?? [])
            .FirstOrDefault(r => r.Title.Equals(settings.Title, StringComparison.OrdinalIgnoreCase));

        if (rec == null)
            throw new InvalidOperationException($"Recommendation not found: {settings.Title}");

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
                throw new ArgumentException($"Unknown field '{settings.Field}'. Valid fields: title, description, state, impact, risk, declineReason");
        }

        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Updated recommendation {settings.Field} to '{settings.Value}'");
        return 0;
    }
}
