using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Wireframes;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanCheckWireframesSettings : CommandSettings
{
    [Description("Plan ID or folder path")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";
}

/// <summary>
///     Runs the wireframe leak check on demand: the same check Tendril applies before a plan reaches
///     Review, a PR or Completed. Exits 1 and lists every finding when the plan's changes carry wireframe
///     code, so an agent can see exactly what the gate saw and fix it.
/// </summary>
public class PlanCheckWireframesCommand : Command<PlanCheckWireframesSettings>
{
    protected override int Execute(CommandContext context, PlanCheckWireframesSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var leaks = PlanWireframeGuard.Check(planFolder);

        if (leaks.Count == 0)
        {
            Console.WriteLine("No wireframe code found in this plan's changes.");
            return 0;
        }

        Console.Error.WriteLine(WireframeLeakGuard.Describe(leaks));
        return 1;
    }
}
