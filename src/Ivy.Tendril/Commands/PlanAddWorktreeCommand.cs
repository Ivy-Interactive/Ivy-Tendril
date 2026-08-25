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
        var relWorktreePath = GitHelper.DeriveWorktreeRelativePath(settings.Repo);
        var worktreePath = Path.Combine(planFolder, "Worktrees", relWorktreePath);

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

        if (Directory.Exists(worktreePath))
        {
            var (removeExitCode, _, removeStdErr) = GitHelper.RunGit($"worktree remove --force \"{worktreePath}\"", settings.Repo);
            if (removeExitCode != 0)
            {
                AnsiConsole.MarkupLine($"[red]Failed to remove existing worktree at {worktreePath.EscapeMarkup()}:[/]");
                AnsiConsole.MarkupLine(removeStdErr.EscapeMarkup());
                return 1;
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
