using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Guards plan creation against a project mismatch: when a plan is created from a GitHub
///     issue/PR <c>SourceUrl</c>, the issue's repo must belong to the chosen project. This is the
///     cheapest place to catch "the plan was created in the wrong project" (issue #1340) — before
///     any execution, worktree, or PR happens.
/// </summary>
public static class PlanSourceProjectGuard
{
    /// <summary>
    ///     Throws <see cref="ArgumentException" /> when <paramref name="sourceUrl" /> is a GitHub
    ///     issue/PR URL whose repo is not part of <paramref name="project" />.
    ///     No-ops when there is no source URL, it isn't a GitHub issue/PR URL, or none of the
    ///     project's repos could be resolved to a remote (fail open — don't block on offline/local-only
    ///     repos). The error names the project that actually owns the repo, when one can be found.
    /// </summary>
    public static void EnsureSourceUrlMatchesProject(string? sourceUrl, ProjectConfig project, IGithubService github)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return;

        var sourceRepo = GithubService.ParseRepoConfigFromIssueOrPrUrl(sourceUrl);
        if (sourceRepo is null)
            return;

        var ownerRepo = sourceRepo.FullName;

        var resolvedProjectRepos = github.GetResolvedGithubRepos(project);
        if (resolvedProjectRepos.Count == 0)
            return; // Fail open: can't determine the project's repos (no origin / offline).

        var projectOwnsSource = resolvedProjectRepos
            .Any(n => n.Equals(ownerRepo, StringComparison.OrdinalIgnoreCase));
        if (projectOwnsSource)
            return;

        var owningProject = github.FindProjectForGithubRepo(ownerRepo);
        var hint = owningProject is not null
            ? $" It belongs to project '{owningProject.Name}' — create the plan there, or remove the source URL."
            : " It is not part of any configured project — add the repo to this project, or remove the source URL.";

        throw new ArgumentException(
            $"Source issue/PR is in '{ownerRepo}', which is not part of project '{project.Name}'.{hint}");
    }
}
