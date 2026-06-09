using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanCleanupSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [CommandOption("--force")]
    [Description("Skip terminal-state and grace-period checks")]
    public bool Force { get; init; }
}

public class PlanCleanupCommand : Command<PlanCleanupSettings>
{
    protected override int Execute(CommandContext context, PlanCleanupSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var terminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { nameof(PlanStatus.Completed), nameof(PlanStatus.Failed), nameof(PlanStatus.Skipped), nameof(PlanStatus.Icebox) };

        if (!settings.Force && !terminalStates.Contains(plan.State))
            throw new InvalidOperationException($"Plan is not in a terminal state (current: {plan.State}). Use --force to override.");

        var worktreesDir = Path.Combine(planFolder, "Worktrees");
        if (!Directory.Exists(worktreesDir) || Directory.GetDirectories(worktreesDir).Length == 0)
        {
            AnsiConsole.MarkupLine("[green]No worktrees to clean up.[/]");
            return 0;
        }

        WorktreeCleanupService.RemoveWorktrees(planFolder);

        var remaining = Directory.Exists(worktreesDir) ? Directory.GetDirectories(worktreesDir).Length : 0;
        if (remaining == 0)
        {
            AnsiConsole.MarkupLine("[green]Worktrees cleaned up successfully.[/]");
        }
        else
        {
            throw new InvalidOperationException($"{remaining} {(remaining == 1 ? "worktree" : "worktrees")} could not be removed.");
        }

        return 0;
    }
}
