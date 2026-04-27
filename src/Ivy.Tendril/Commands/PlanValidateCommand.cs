using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanValidateSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";
}

public class PlanValidateCommand : Command<PlanValidateSettings>
{
    private readonly ILogger<PlanValidateCommand> _logger;

    public PlanValidateCommand(ILogger<PlanValidateCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanValidateSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Validate the plan
            PlanValidationService.Validate(plan);

            _logger.LogInformation("Plan {PlanId} is valid", settings.PlanId);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for plan {PlanId}", settings.PlanId);
            return 1;
        }
    }
}
