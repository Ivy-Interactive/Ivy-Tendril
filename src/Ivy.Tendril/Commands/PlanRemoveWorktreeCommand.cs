using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanRemoveWorktreeSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Repository folder name within the plan's worktrees directory")]
    [CommandArgument(1, "<repo-name>")]
    public string RepoName { get; set; } = "";

    [CommandOption("--branch")]
    [Description("Branch name to delete (auto-derived from plan if not specified)")]
    public string? Branch { get; init; }
}

public class PlanRemoveWorktreeCommand : Command<PlanRemoveWorktreeSettings>
{
    private readonly ILogger<PlanRemoveWorktreeCommand> _logger;

    public PlanRemoveWorktreeCommand(ILogger<PlanRemoveWorktreeCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanRemoveWorktreeSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
            var worktreePath = Path.Combine(planFolder, "worktrees", settings.RepoName);

            if (!Directory.Exists(worktreePath))
            {
                AnsiConsole.MarkupLine($"[yellow]Worktree directory not found: {worktreePath.EscapeMarkup()}[/]");
                return 0;
            }

            var branchName = settings.Branch ?? DeriveBranchName(planFolder);
            var repoRoot = ResolveRepoRoot(worktreePath);

            if (repoRoot != null)
            {
                var psi = new ProcessStartInfo("git", $"worktree remove --force \"{worktreePath}\"")
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                process?.WaitForExit(30000);

                if (!Directory.Exists(worktreePath))
                {
                    DeleteBranch(repoRoot, branchName);
                    AnsiConsole.MarkupLine("[green]Worktree removed successfully.[/]");
                    return 0;
                }
            }

            WorktreeCleanupService.ForceDeleteDirectory(worktreePath, _logger);

            if (!Directory.Exists(worktreePath))
            {
                if (repoRoot != null)
                    DeleteBranch(repoRoot, branchName);
                AnsiConsole.MarkupLine("[green]Worktree removed (force delete).[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[red]Failed to remove worktree.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove worktree for plan {PlanId}", settings.PlanId);
            return 1;
        }
    }

    private static string? ResolveRepoRoot(string worktreePath)
    {
        var gitFile = Path.Combine(worktreePath, ".git");
        if (!File.Exists(gitFile)) return null;

        var content = File.ReadAllText(gitFile).Trim();
        var match = Regex.Match(content, @"gitdir:\s*(.+)");
        if (!match.Success) return null;

        var gitDir = match.Groups[1].Value.Trim();
        var repoGitDir = Path.GetFullPath(Path.Combine(gitDir, "..", ".."));
        var repoRoot = Path.GetDirectoryName(repoGitDir);

        return repoRoot != null && Directory.Exists(repoRoot) ? repoRoot : null;
    }

    private static string DeriveBranchName(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"tendril/{folderName}";
    }

    private static void DeleteBranch(string repoRoot, string branchName)
    {
        var psi = new ProcessStartInfo("git", $"branch -D \"{branchName}\"")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(10000);
    }
}
