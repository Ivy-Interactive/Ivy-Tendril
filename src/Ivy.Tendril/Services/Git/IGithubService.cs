namespace Ivy.Tendril.Services.Git;

public record GitHubIssue(
    int Number,
    string Title,
    string? Body,
    string[] Labels,
    string[] Assignees
);

public interface IGithubService
{
    List<RepoConfig> GetRepos();
    RepoConfig? GetRepoConfigFromPathCached(string repoPath);

    /// <summary>
    ///     Returns the single configured project whose repos include the GitHub repo
    ///     <paramref name="ownerRepo" /> (format <c>owner/name</c>), or null when zero or more
    ///     than one project matches (unconfigured / ambiguous).
    /// </summary>
    ProjectConfig? FindProjectForGithubRepo(string ownerRepo);

    /// <summary>
    ///     The resolved <c>owner/name</c> of each of the project's repos. Repos whose remote can't
    ///     be resolved are omitted; an empty list means none could be resolved (callers fail open).
    /// </summary>
    IReadOnlyList<string> GetResolvedGithubRepos(ProjectConfig project);
    Task<(List<string> assignees, string? error)> GetAssigneesAsync(string owner, string repo);
    Task<(List<string> labels, string? error)> GetLabelsAsync(string owner, string repo);
    Task<(Dictionary<string, string> statuses, string? error)> GetPrStatusesAsync(string owner, string repo);
    Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request);
}
