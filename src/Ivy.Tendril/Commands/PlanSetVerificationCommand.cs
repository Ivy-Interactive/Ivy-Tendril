using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanSetVerificationSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";

    [Description("Verification status (Pending, Pass, Fail, Skipped)")]
    [CommandArgument(2, "<status>")]
    public string Status { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Name, "name"),
            CliValidation.RequireNonEmpty(Status, "status"),
            CliValidation.ValidateOneOf(Status, "<status>", CliValidation.ValidVerificationStatuses)
        );
    }
}

public class PlanSetVerificationCommand : Command<PlanSetVerificationSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanSetVerificationCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanSetVerificationSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var verification = plan.Verifications.FirstOrDefault(v =>
            v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

        var status = VerificationStatusExtensions.Parse(settings.Status);
        if (verification != null)
        {
            verification.Status = status;
        }
        else
        {
            plan.Verifications.Add(new PlanVerificationEntry
            {
                Name = settings.Name,
                Status = status
            });
        }

        plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Set verification {settings.Name} = {settings.Status}");
        return 0;
    }
}
