using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanSyncWorktreeSettings : CommandSettings
{
    [Description("Absolute path to the worktree directory")]
    [CommandArgument(0, "<worktree-path>")]
    public string WorktreePath { get; set; } = "";

    [CommandOption("--strategy")]
    [Description("Sync strategy: fetch (default/no-op), rebase, or merge")]
    public string Strategy { get; init; } = "fetch";

    [CommandOption("--base-branch")]
    [Description("Base branch to sync with (e.g., main, development)")]
    public string BaseBranch { get; init; } = "";
}

public class PlanSyncWorktreeCommand : Command<PlanSyncWorktreeSettings>
{
    private readonly ILogger<PlanSyncWorktreeCommand> _logger;

    public PlanSyncWorktreeCommand(ILogger<PlanSyncWorktreeCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanSyncWorktreeSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(settings.WorktreePath))
            {
                AnsiConsole.MarkupLine($"[red]Worktree path does not exist: {settings.WorktreePath.EscapeMarkup()}[/]");
                return 1;
            }

            var strategy = settings.Strategy.ToLowerInvariant();

            switch (strategy)
            {
                case "fetch":
                    AnsiConsole.MarkupLine("[green]Sync strategy 'fetch': no additional action needed.[/]");
                    return 0;

                case "rebase":
                    if (string.IsNullOrEmpty(settings.BaseBranch))
                    {
                        AnsiConsole.MarkupLine("[red]--base-branch is required for rebase strategy.[/]");
                        return 1;
                    }
                    return RunSync(settings.WorktreePath, settings.BaseBranch, useRebase: true);

                case "merge":
                    if (string.IsNullOrEmpty(settings.BaseBranch))
                    {
                        AnsiConsole.MarkupLine("[red]--base-branch is required for merge strategy.[/]");
                        return 1;
                    }
                    return RunSync(settings.WorktreePath, settings.BaseBranch, useRebase: false);

                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown strategy '{strategy.EscapeMarkup()}', treating as 'fetch' (no-op).[/]");
                    return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync worktree at {WorktreePath}", settings.WorktreePath);
            return 1;
        }
    }

    private static int RunSync(string worktreePath, string baseBranch, bool useRebase)
    {
        var fetchResult = RunGit(worktreePath, "fetch origin");
        if (fetchResult != 0)
        {
            AnsiConsole.MarkupLine("[red]git fetch failed.[/]");
            return fetchResult;
        }

        var syncArgs = useRebase
            ? $"rebase origin/{baseBranch}"
            : $"merge origin/{baseBranch} --no-edit";

        var syncResult = RunGit(worktreePath, syncArgs);
        if (syncResult != 0)
        {
            var op = useRebase ? "rebase" : "merge";
            AnsiConsole.MarkupLine($"[red]git {op} failed.[/]");
            return syncResult;
        }

        var op2 = useRebase ? "Rebase" : "Merge";
        AnsiConsole.MarkupLine($"[green]{op2} completed successfully.[/]");
        return 0;
    }

    private static int RunGit(string workingDirectory, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return 1;
        process.WaitForExit(60000);
        return process.ExitCode;
    }
}
