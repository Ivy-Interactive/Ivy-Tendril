using System.ComponentModel;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
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
}

public class PlanSetVerificationCommand : Command<PlanSetVerificationSettings>
{
    private readonly ILogger<PlanSetVerificationCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanSetVerificationCommand(ILogger<PlanSetVerificationCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanSetVerificationSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Find existing verification
            var verification = plan.Verifications.FirstOrDefault(v =>
                v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (verification != null)
            {
                // Update existing
                verification.Status = settings.Status;
            }
            else
            {
                // Add new
                plan.Verifications.Add(new PlanVerificationEntry
                {
                    Name = settings.Name,
                    Status = settings.Status
                });
            }

            plan.Updated = DateTime.UtcNow;

            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Set verification '{Name}' to '{Status}'", settings.Name, settings.Status);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set verification on plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
