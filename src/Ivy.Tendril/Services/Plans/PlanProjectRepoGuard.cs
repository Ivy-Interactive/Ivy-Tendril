using Ivy.Tendril.Services;

namespace Ivy.Tendril.Services.Plans;

/// <summary>
///     Guards against a plan referencing a repository that isn't part of its project. Without this,
///     a plan in project A can accumulate (or be created with) a repo from project B and end up
///     executing / merging there (issue #1340).
/// </summary>
public static class PlanProjectRepoGuard
{
    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if any of <paramref name="repoPaths" /> is not part
    ///     of <paramref name="project" />'s configured repos or build dependencies. Membership is by
    ///     repo folder name (case-insensitive), matching how <c>JobLauncher.FindProjectRepoConfig</c>
    ///     resolves project membership when building the firmware <c>RepoConfigs</c> — so the guard and
    ///     the executor agree on what "belongs to the project" means.
    /// </summary>
    public static void EnsureReposBelongToProject(IEnumerable<string> repoPaths, ProjectConfig project)
    {
        var allowed = AllowedRepoNames(project);

        // Fail open: a project with no repos configured gives nothing to validate against.
        if (allowed.Count == 0)
            return;

        var offending = repoPaths
            .Select(RepoName)
            .Where(name => !string.IsNullOrEmpty(name) && !allowed.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (offending.Count == 0)
            return;

        var allowedList = allowed.Count > 0
            ? string.Join(", ", allowed.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            : "(none)";
        throw new ArgumentException(
            $"Repo(s) [{string.Join(", ", offending)}] are not part of project '{project.Name}'. " +
            $"Allowed repos: [{allowedList}].");
    }

    private static HashSet<string> AllowedRepoNames(ProjectConfig project)
    {
        var names = project.RepoPaths
            .Concat(project.BuildDependencies)
            .Select(RepoName)
            .Where(n => !string.IsNullOrEmpty(n));
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    private static string RepoName(string path)
    {
        var cleaned = Environment.ExpandEnvironmentVariables(path).TrimEnd('/', '\\');
        var lastSlash = cleaned.LastIndexOfAny(['/', '\\']);
        return lastSlash >= 0 ? cleaned[(lastSlash + 1)..] : cleaned;
    }
}
