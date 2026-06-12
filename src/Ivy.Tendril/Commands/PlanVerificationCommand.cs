using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanVerificationListSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [CommandOption("--status")]
    [Description("Filter by status (Pending, Pass, Fail, Skipped)")]
    public string? Status { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.ValidateOneOf(Status, "--status", CliValidation.ValidVerificationStatuses)
        );
    }
}

public class PlanVerificationAddSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    [CommandOption("--status")]
    [Description("Initial status (default: Pending). Valid values: Pending, Pass, Fail, Skipped")]
    public string? Status { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.ValidateOneOf(Status, "--status", CliValidation.ValidVerificationStatuses)
        );
    }
}

public class PlanVerificationRemoveSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Name, "name")
        );
    }
}

public class PlanVerificationListCommand : Command<PlanVerificationListSettings>
{
    protected override int Execute(CommandContext context, PlanVerificationListSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var verifications = plan.Verifications;

        if (!string.IsNullOrEmpty(settings.Status))
            verifications = verifications.Where(v => v.Status.Equals(settings.Status, StringComparison.OrdinalIgnoreCase)).ToList();

        if (verifications.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No verifications found.[/]");
            return 0;
        }

        var table = new Spectre.Console.Table();
        table.AddColumn("Name");
        table.AddColumn("Status");

        foreach (var v in verifications)
            table.AddRow(v.Name.EscapeMarkup(), v.Status.EscapeMarkup());

        AnsiConsole.Write(table);
        return 0;
    }
}

public class PlanVerificationAddCommand : Command<PlanVerificationAddSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanVerificationAddCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanVerificationAddSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        if (plan.Verifications.Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Verification already exists: {settings.Name}");

        plan.Verifications.Add(new PlanVerificationEntry
        {
            Name = settings.Name,
            Status = settings.Status ?? VerificationStatus.Pending
        });

        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Added verification: {settings.Name}");
        return 0;
    }
}

public class PlanVerificationRemoveCommand : Command<PlanVerificationRemoveSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanVerificationRemoveCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanVerificationRemoveSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var match = plan.Verifications
            .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        if (match == null)
            throw new InvalidOperationException($"Verification not found: {settings.Name}");

        plan.Verifications.Remove(match);
        plan.Updated = DateTime.UtcNow;
        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Removed verification: {settings.Name}");
        return 0;
    }
}
