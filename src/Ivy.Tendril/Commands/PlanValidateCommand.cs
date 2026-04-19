using System.ComponentModel;
using Ivy.Tendril.Services;
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
    public override int Execute(CommandContext context, PlanValidateSettings settings)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            // Validate the plan
            PlanValidationService.Validate(plan);

            Console.WriteLine($"Plan {settings.PlanId} is valid");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Validation failed: {ex.Message}");
            return 1;
        }
    }
}
