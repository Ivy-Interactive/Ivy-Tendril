using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
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
    [Description("Initial status (default: Pending)")]
    public string? Status { get; set; }
}

public class PlanVerificationRemoveSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Verification name")]
    [CommandArgument(1, "<name>")]
    public string Name { get; set; } = "";
}

public class PlanVerificationListCommand : Command<PlanVerificationListSettings>
{
    private readonly ILogger<PlanVerificationListCommand> _logger;

    public PlanVerificationListCommand(ILogger<PlanVerificationListCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanVerificationListSettings settings, CancellationToken cancellationToken)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list verifications for plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}

public class PlanVerificationAddCommand : Command<PlanVerificationAddSettings>
{
    private readonly ILogger<PlanVerificationAddCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanVerificationAddCommand(ILogger<PlanVerificationAddCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanVerificationAddSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            if (plan.Verifications.Any(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogError("Verification already exists: {Name}", settings.Name);
                return 1;
            }

            plan.Verifications.Add(new PlanVerificationEntry
            {
                Name = settings.Name,
                Status = settings.Status ?? "Pending"
            });

            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Added verification: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add verification to plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}

public class PlanVerificationRemoveCommand : Command<PlanVerificationRemoveSettings>
{
    private readonly ILogger<PlanVerificationRemoveCommand> _logger;
    private readonly IPlanWatcherService _planWatcher;

    public PlanVerificationRemoveCommand(ILogger<PlanVerificationRemoveCommand> logger, IPlanWatcherService planWatcher)
    {
        _logger = logger;
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanVerificationRemoveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var match = plan.Verifications
                .FirstOrDefault(v => v.Name.Equals(settings.Name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                _logger.LogError("Verification not found: {Name}", settings.Name);
                return 1;
            }

            plan.Verifications.Remove(match);
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

            _logger.LogInformation("Removed verification: {Name}", settings.Name);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove verification from plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
