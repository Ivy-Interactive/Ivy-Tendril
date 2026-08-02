using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanAddWorktreeSettings : CommandSettings
{
    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Absolute path to the source repository")]
    [CommandArgument(1, "<repo>")]
    public string Repo { get; set; } = "";

    [CommandOption("--base <BRANCH>")]
    [Description("Base branch/ref to branch from (default: auto-detected default branch)")]
    public string? Base { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.RequireNonEmpty(Repo, "repo"));
    }
}

public class PlanAddWorktreeCommand : Command<PlanAddWorktreeSettings>
{
    private readonly ILogger<PlanAddWorktreeCommand> _logger;

    public PlanAddWorktreeCommand(ILogger<PlanAddWorktreeCommand> logger) => _logger = logger;

    protected override int Execute(CommandContext context, PlanAddWorktreeSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        if (!Directory.Exists(settings.Repo))
        {
            AnsiConsole.MarkupLine($"[red]Repo path does not exist: {settings.Repo.EscapeMarkup()}[/]");
            return 1;
        }

        var branchName = DeriveBranchName(planFolder);
        var repoName = Path.GetFileName(settings.Repo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var worktreePath = Path.Combine(planFolder, "Worktrees", repoName);

        Directory.CreateDirectory(Path.Combine(planFolder, "Worktrees"));

        if (Directory.Exists(worktreePath))
        {
            // `git worktree remove --force` exits 0 even when it leaves the directory skeleton
            // behind: measured on git 2.54 against a real pnpm tree, 1,132 directories survived a
            // successful removal because the tree contains symlinks and NTFS junctions. Git's own
            // metadata does go, so `worktree list` and `worktree prune` both consider the worktree
            // gone, and only the leftover directory remains to make `worktree add` fail later with
            // a misleading `fatal: ... already exists`. So the exit code is not the answer here:
            // whether the directory is gone is. A 650 MB tree took 41 s to remove, close enough to
            // RunGit's 60 s default to warrant an explicit longer timeout.
            var (_, _, removeStdErr) = GitHelper.RunGit($"worktree remove --force \"{worktreePath}\"", settings.Repo, 180000);

            if (Directory.Exists(worktreePath))
            {
                var forceDeleteError = "";
                try
                {
                    WorktreeCleanupService.ForceDeleteDirectory(worktreePath, _logger);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    forceDeleteError = ex.Message;
                }

                if (Directory.Exists(worktreePath))
                {
                    AnsiConsole.MarkupLine($"[red]Failed to remove existing worktree at {worktreePath.EscapeMarkup()}:[/]");
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(removeStdErr)
                        ? "git worktree remove produced no stderr."
                        : $"git worktree remove reported: {removeStdErr.Trim().EscapeMarkup()}");
                    AnsiConsole.MarkupLine(string.IsNullOrWhiteSpace(forceDeleteError)
                        ? "The force delete completed without error but the directory is still present."
                        : $"Force delete failed: {forceDeleteError.EscapeMarkup()}");
                    return 1;
                }

                // Deleting the directory ourselves bypasses git, so if the removal above did not
                // finish (a timeout kills it mid-delete, which is the other reason its exit code is
                // ignored) the worktree stays registered under .git/worktrees and `add` then fails
                // with `is a missing but already registered worktree`. Prune clears that; it is a
                // no-op when the removal did complete.
                GitHelper.RunGit("worktree prune", settings.Repo, 30000);
            }

            // Re-executing a plan is a normal occurrence (ExecutePlan re-runs after review
            // comments), so the branch from the prior run must not block a fresh `-b` create.
            // Best-effort: ignore failure (e.g. branch already gone).
            GitHelper.RunGit($"branch -D \"{branchName}\"", settings.Repo);
        }

        var (fetchExitCode, _, fetchStdErr) = GitHelper.RunGit("fetch origin", settings.Repo);
        if (fetchExitCode != 0)
        {
            AnsiConsole.MarkupLine($"[red]git fetch origin failed in {settings.Repo.EscapeMarkup()}:[/]");
            AnsiConsole.MarkupLine(fetchStdErr.EscapeMarkup());
            return 1;
        }

        string baseBranch;
        if (!string.IsNullOrEmpty(settings.Base))
        {
            baseBranch = settings.Base;
        }
        else
        {
            var (headExitCode, headStdOut, headStdErr) = GitHelper.RunGit("symbolic-ref refs/remotes/origin/HEAD", settings.Repo);
            if (headExitCode != 0)
            {
                AnsiConsole.MarkupLine("[red]Could not auto-detect default branch (pass --base explicitly):[/]");
                AnsiConsole.MarkupLine(headStdErr.EscapeMarkup());
                return 1;
            }

            baseBranch = headStdOut.Trim().Replace("refs/remotes/origin/", "");
        }

        var (addExitCode, _, addStdErr) = GitHelper.RunGit(
            $"worktree add \"{worktreePath}\" -b \"{branchName}\" \"origin/{baseBranch}\"", settings.Repo);
        if (addExitCode != 0)
        {
            AnsiConsole.MarkupLine("[red]git worktree add failed:[/]");
            AnsiConsole.MarkupLine(addStdErr.EscapeMarkup());
            return 1;
        }

        if (!File.Exists(Path.Combine(worktreePath, ".git")))
        {
            AnsiConsole.MarkupLine($"[red]git worktree add reported success but {worktreePath.EscapeMarkup()}/.git is missing - worktree is not usable.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Worktree created: {worktreePath.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLine($"[green]Branch: {branchName.EscapeMarkup()}[/]");
        return 0;
    }

    private static string DeriveBranchName(string planFolder)
    {
        var folderName = Path.GetFileName(planFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"tendril/{folderName}";
    }
}
