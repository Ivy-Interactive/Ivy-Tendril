using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class ResetSettings : CommandSettings
{
    [CommandOption("--force")]
    [Description("Skip confirmation prompt")]
    public bool Force { get; init; }
}

public class ResetCommand : Command<ResetSettings>
{
    private readonly ILogger<ResetCommand> _logger;

    public ResetCommand(ILogger<ResetCommand> logger)
    {
        _logger = logger;
    }

    protected override int Execute(CommandContext context, ResetSettings settings, CancellationToken cancellationToken)
    {
        // Step 1: Gather targets
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        var tendrilPlans = Environment.GetEnvironmentVariable("TENDRIL_PLANS");

        var targets = new List<(string Type, string Description, bool Exists)>();

        // Check TENDRIL_HOME directory
        if (!string.IsNullOrEmpty(tendrilHome))
        {
            bool exists = Directory.Exists(tendrilHome);
            string description = tendrilHome;
            if (exists)
            {
                int fileCount = Directory.GetFiles(tendrilHome, "*", SearchOption.AllDirectories).Length;
                description += $" (exists, {fileCount} files)";
            }
            targets.Add(("Directory", description, exists));
        }

        // Check TENDRIL_PLANS directory
        if (!string.IsNullOrEmpty(tendrilPlans) && tendrilPlans != tendrilHome)
        {
            bool exists = Directory.Exists(tendrilPlans);
            string description = tendrilPlans;
            if (exists)
            {
                int folderCount = Directory.GetDirectories(tendrilPlans, "*", SearchOption.TopDirectoryOnly).Length;
                description += $" (exists, {folderCount} folders)";
            }
            targets.Add(("Directory", description, exists));
        }

        // Check environment variables
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        if (isWindows)
        {
            var tendrilHomeEnv = Environment.GetEnvironmentVariable("TENDRIL_HOME", EnvironmentVariableTarget.User);
            if (!string.IsNullOrEmpty(tendrilHomeEnv))
            {
                targets.Add(("Env var", "TENDRIL_HOME (User)", true));
            }

            var tendrilPlansEnv = Environment.GetEnvironmentVariable("TENDRIL_PLANS", EnvironmentVariableTarget.User);
            if (!string.IsNullOrEmpty(tendrilPlansEnv))
            {
                targets.Add(("Env var", "TENDRIL_PLANS (User)", true));
            }
        }
        else
        {
            // On Linux/Mac, we can't automatically detect shell rc env vars
            if (!string.IsNullOrEmpty(tendrilHome))
            {
                targets.Add(("Env var", "TENDRIL_HOME (check shell rc)", true));
            }
            if (!string.IsNullOrEmpty(tendrilPlans))
            {
                targets.Add(("Env var", "TENDRIL_PLANS (check shell rc)", true));
            }
        }

        // Step 2: Present summary
        if (targets.Count == 0 || !targets.Any(t => t.Exists))
        {
            AnsiConsole.MarkupLine("[green]Nothing to reset.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[yellow]The following items will be deleted:[/]");
        AnsiConsole.WriteLine();
        foreach (var target in targets.Where(t => t.Exists))
        {
            AnsiConsole.MarkupLine($"[yellow]{target.Type}:[/] {target.Description}");
        }
        AnsiConsole.WriteLine();

        // Step 3: Confirm
        if (!settings.Force)
        {
            if (!AnsiConsole.Confirm("Proceed with reset?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[dim]Cancelled.[/]");
                return 0;
            }
        }

        // Step 4: Delete
        var errors = new List<string>();

        // Delete TENDRIL_HOME directory
        if (!string.IsNullOrEmpty(tendrilHome) && Directory.Exists(tendrilHome))
        {
            try
            {
                Directory.Delete(tendrilHome, recursive: true);
                AnsiConsole.MarkupLine($"[green]✓[/] Deleted directory: {tendrilHome}");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to delete {tendrilHome}: {ex.Message}");
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to delete directory: {tendrilHome}");
                _logger.LogError(ex, "Failed to delete TENDRIL_HOME directory");
            }
        }

        // Delete TENDRIL_PLANS directory
        if (!string.IsNullOrEmpty(tendrilPlans) && tendrilPlans != tendrilHome && Directory.Exists(tendrilPlans))
        {
            try
            {
                Directory.Delete(tendrilPlans, recursive: true);
                AnsiConsole.MarkupLine($"[green]✓[/] Deleted directory: {tendrilPlans}");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to delete {tendrilPlans}: {ex.Message}");
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to delete directory: {tendrilPlans}");
                _logger.LogError(ex, "Failed to delete TENDRIL_PLANS directory");
            }
        }

        // Remove environment variables
        if (isWindows)
        {
            try
            {
                Environment.SetEnvironmentVariable("TENDRIL_HOME", null, EnvironmentVariableTarget.User);
                AnsiConsole.MarkupLine("[green]✓[/] Removed env var: TENDRIL_HOME (User)");
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to remove TENDRIL_HOME env var: {ex.Message}");
                AnsiConsole.MarkupLine("[red]✗[/] Failed to remove env var: TENDRIL_HOME");
                _logger.LogError(ex, "Failed to remove TENDRIL_HOME environment variable");
            }

            try
            {
                var tendrilPlansEnv = Environment.GetEnvironmentVariable("TENDRIL_PLANS", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(tendrilPlansEnv))
                {
                    Environment.SetEnvironmentVariable("TENDRIL_PLANS", null, EnvironmentVariableTarget.User);
                    AnsiConsole.MarkupLine("[green]✓[/] Removed env var: TENDRIL_PLANS (User)");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to remove TENDRIL_PLANS env var: {ex.Message}");
                AnsiConsole.MarkupLine("[red]✗[/] Failed to remove env var: TENDRIL_PLANS");
                _logger.LogError(ex, "Failed to remove TENDRIL_PLANS environment variable");
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Note:[/] On Linux/Mac, please manually remove the export lines from your shell rc file:");
            AnsiConsole.MarkupLine("  [dim]export TENDRIL_HOME=...[/]");
            AnsiConsole.MarkupLine("  [dim]export TENDRIL_PLANS=...[/]");
        }

        // Step 5: Remind
        AnsiConsole.WriteLine();
        if (errors.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Reset complete.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Reset completed with errors.[/]");
            foreach (var error in errors)
            {
                AnsiConsole.MarkupLine($"[red]- {error}[/]");
            }
        }

        AnsiConsole.MarkupLine("[yellow]Please restart your terminal for environment variable changes to take effect.[/]");

        return errors.Count > 0 ? 1 : 0;
    }
}
