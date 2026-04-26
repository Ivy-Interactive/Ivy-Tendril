using System.ComponentModel;
using Ivy.Tendril.Services;
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
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var plan = PlanCommandHelpers.ReadPlan(planFolder);

            var terminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Completed", "Failed", "Skipped", "Icebox" };

            if (!settings.Force && !terminalStates.Contains(plan.State))
            {
                AnsiConsole.MarkupLine($"[yellow]Plan is not in a terminal state (current: {plan.State.EscapeMarkup()}). Use --force to override.[/]");
                return 1;
            }

            var worktreesDir = Path.Combine(planFolder, "worktrees");
            if (!Directory.Exists(worktreesDir) || Directory.GetDirectories(worktreesDir).Length == 0)
            {
                AnsiConsole.MarkupLine("[green]No worktrees to clean up.[/]");
                return 0;
            }

            PlanReaderService.RemoveWorktrees(planFolder);

            var remaining = Directory.Exists(worktreesDir) ? Directory.GetDirectories(worktreesDir).Length : 0;
            if (remaining == 0)
            {
                AnsiConsole.MarkupLine("[green]Worktrees cleaned up successfully.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{remaining} worktree(s) could not be removed.[/]");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
