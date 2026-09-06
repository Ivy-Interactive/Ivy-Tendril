namespace Ivy.Tendril.Services.Git;

public record GitHubIssue(
    int Number,
    string Title,
    string? Body,
    string[] Labels,
    string[] Assignees
);

/// <summary>
///     A PR's cached status and the branch (head ref) it was opened from. <see cref="Branch" /> is
///     <c>""</c> when GitHub reported no head ref (or the field was absent).
/// </summary>
public record PrInfo(string Status, string Branch);

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

    /// <summary>
    ///     One PR resolved on its own, for the callers that must not depend on a recent PR window. Returns
    ///     (null, error) when gh could not answer: a deleted PR, a repo the token cannot read and a rate
    ///     limit all arrive this way, and none of them is a status.
    /// </summary>
    Task<(PrInfo? info, string? error)> GetPrStatusAsync(string prUrl);

    Task<(List<GitHubIssue> issues, string? error)> SearchIssuesAsync(IssueSearchRequest request);
}
