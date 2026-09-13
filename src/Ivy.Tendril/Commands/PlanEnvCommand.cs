using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanEnvMaterializeSettings : CommandSettings
{
    [Description("Plan ID (e.g., 00105)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [CommandOption("--repo <repo>")]
    [Description("Only materialize into this repo's worktree (path or repo name; default: every worktree)")]
    public string? Repo { get; set; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanEnvGetSettings : CommandSettings
{
    [Description("Plan ID (e.g., 00105)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

/// <summary>
///     Allocates the plan's service ports and writes the project's configured environment files into
///     its worktrees. Run automatically by <see cref="PlanAddWorktreeCommand" />; exposed separately so
///     a worktree that already exists can be refreshed after the project's <c>envFiles</c> change.
/// </summary>
public class PlanEnvMaterializeCommand : Command<PlanEnvMaterializeSettings>
{
    protected override int Execute(CommandContext context, PlanEnvMaterializeSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var project = PlanEnvHelpers.ResolveProject(plan);

        var allocatedPorts = PortAllocationHelper.AllocatePorts(project, plan, planFolder);
        foreach (var (name, port) in allocatedPorts.OrderBy(p => p.Key, StringComparer.Ordinal))
            AnsiConsole.MarkupLine($"[green]Port {name.EscapeMarkup()}: {port}[/]");

        var worktrees = PlanEnvHelpers.ResolveWorktrees(plan, planFolder, settings.Repo);
        if (worktrees.Count == 0)
        {
            AnsiConsole.MarkupLine(settings.Repo is null
                ? "[yellow]No worktrees found for this plan - run 'tendril plan add-worktree' first.[/]"
                : $"[yellow]No worktree found for repo {settings.Repo.EscapeMarkup()}.[/]");
            return 1;
        }

        if (project.EnvFiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No environment files configured for this project.[/]");
            return 0;
        }

        foreach (var worktree in worktrees)
        {
            var written = EnvironmentMaterializationHelper.MaterializeEnvFiles(project, allocatedPorts, worktree);
            AnsiConsole.MarkupLine(
                $"[green]Materialized {written} environment file(s) into {worktree.EscapeMarkup()}.[/]");
        }

        return 0;
    }
}

/// <summary>
///     Prints what a plan's worktree receives: its allocated ports and, per configured environment
///     file, the key/value pairs the template and overrides resolve to. Read-only - it neither
///     allocates a port nor writes a file, so it is safe to run against a plan under review.
/// </summary>
public class PlanEnvGetCommand : Command<PlanEnvGetSettings>
{
    protected override int Execute(CommandContext context, PlanEnvGetSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);
        var project = PlanEnvHelpers.ResolveProject(plan);
        var allocatedPorts = plan.AllocatedPorts ?? new Dictionary<string, int>();

        if (allocatedPorts.Count == 0)
            AnsiConsole.MarkupLine("[dim]No ports allocated for this plan.[/]");
        else
            CliOutput.WriteTable(
                ["Port", "Value"],
                allocatedPorts
                    .OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new[] { p.Key, p.Value.ToString() }));

        if (project.EnvFiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No environment files configured for this project.[/]");
            return 0;
        }

        // Values are resolved against the first worktree, since a template is read relative to it.
        // Without one the templates are simply absent and only the overrides are shown.
        var worktree = PlanEnvHelpers.ResolveWorktrees(plan, planFolder, null).FirstOrDefault() ?? planFolder;

        foreach (var envFile in project.EnvFiles)
        {
            var values = EnvironmentMaterializationHelper.ResolveEnvValues(envFile, project, allocatedPorts, worktree);
            AnsiConsole.MarkupLine($"[bold]{envFile.Path.EscapeMarkup()}[/]");
            if (values.Count == 0)
            {
                AnsiConsole.MarkupLine("[dim]  (empty)[/]");
                continue;
            }

            foreach (var (key, value) in values)
                Console.WriteLine($"  {key}={value}");
        }

        return 0;
    }
}

internal static class PlanEnvHelpers
{
    /// <summary>Resolves the plan's project config, or throws with the configured project names.</summary>
    public static ProjectConfig ResolveProject(PlanYaml plan)
    {
        var config = new ConfigService();
        var project = config.Settings.Projects
            .FirstOrDefault(p => p.Name.Equals(plan.Project, StringComparison.OrdinalIgnoreCase));

        if (project == null)
            CliValidation.ThrowProjectNotFound(plan.Project, config.Settings.Projects.Select(p => p.Name));

        return project;
    }

    /// <summary>
    ///     The existing worktree directories for the plan's repos, filtered to
    ///     <paramref name="repoFilter" /> when given (matched against the repo path or its last segment).
    ///     Repos whose worktree has not been created are skipped rather than reported: a plan commonly
    ///     lists repos it has not needed to check out.
    /// </summary>
    public static List<string> ResolveWorktrees(PlanYaml plan, string planFolder, string? repoFilter)
    {
        var worktrees = new List<string>();

        foreach (var repo in plan.Repos)
        {
            if (repoFilter != null && !MatchesRepo(repo, repoFilter)) continue;

            var worktree = WorktreePathHelper.GetWorktreePath(planFolder, repo);
            if (Directory.Exists(worktree))
                worktrees.Add(worktree);
        }

        return worktrees;
    }

    private static bool MatchesRepo(string repoPath, string filter)
    {
        if (repoPath.Equals(filter, StringComparison.OrdinalIgnoreCase)) return true;
        var name = PathHelper.GetFileNameCrossPlatform(repoPath.TrimEnd('/', '\\'));
        return name.Equals(PathHelper.GetFileNameCrossPlatform(filter.TrimEnd('/', '\\')), StringComparison.OrdinalIgnoreCase);
    }
}
