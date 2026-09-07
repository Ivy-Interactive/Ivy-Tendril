using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.IssueTrackers.Providers.GitHub;

public class GitHubTrackerProvider(
    IGithubService githubService,
    IConfigService config,
    ILogger<GitHubTrackerProvider> logger) : IIssueTrackerProvider, IReviewTrackerProvider
{
    public string ProviderId => "github";
    public string DisplayName => "GitHub";
    public Icons Icon => Icons.Github;

    public Task<bool> IsConfiguredAsync(CancellationToken ct = default)
    {
        // GitHub provider is always available via gh CLI or token
        return Task.FromResult(true);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetMyAssignedIssuesAsync(CancellationToken ct = default)
    {
        try
        {
            var (issues, err) = await githubService.GetMyAssignedIssuesAsync();
            if (err != null)
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(err, []);

            var trackerIssues = issues.Select(i => MapIssue(i, i.Repository)).ToList();
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Success(trackerIssues);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get GitHub assigned issues");
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(ex.Message, []);
        }
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        try
        {
            var reposToSearch = new List<string>();

            if (!string.IsNullOrWhiteSpace(project.IssueTracker?.Repo))
            {
                reposToSearch.Add(project.IssueTracker.Repo.Trim());
            }
            else
            {
                var resolvedRepos = githubService.GetResolvedGithubRepos(project);
                reposToSearch.AddRange(resolvedRepos);
            }

            if (reposToSearch.Count == 0)
            {
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(
                    $"No git remotes resolved for project {project.Name}.", []);
            }

            var allIssues = new List<TrackerIssue>();
            string? firstError = null;

            foreach (var repoStr in reposToSearch)
            {
                var parts = repoStr.Split('/');
                if (parts.Length != 2) continue;
                var owner = parts[0];
                var repoName = parts[1];

                var (issues, err) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                    owner, repoName, Limit: query.Limit));

                if (err != null && firstError == null)
                    firstError = err;

                foreach (var issue in issues)
                {
                    allIssues.Add(MapIssue(issue, repoStr));
                }
            }

            return firstError != null && allIssues.Count == 0
                ? ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(firstError, [])
                : ProviderResult<IReadOnlyList<TrackerIssue>>.Success(allIssues);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get GitHub project issues for {Project}", project.Name);
            return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(ex.Message, []);
        }
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerIssue>>> GetProjectIssuesForTrackerAsync(
        ProjectConfig project,
        ProjectTrackerConfig tracker,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(tracker.Repo))
        {
            var parts = tracker.Repo.Trim().Split('/');
            if (parts.Length == 2)
            {
                var (issues, err) = await githubService.SearchIssuesAsync(new IssueSearchRequest(
                    parts[0], parts[1], Limit: query.Limit));
                if (err != null && issues.Count == 0)
                    return ProviderResult<IReadOnlyList<TrackerIssue>>.Failure(err, []);

                var mapped = issues.Select(i => MapIssue(i, tracker.Repo.Trim())).ToList();
                return ProviderResult<IReadOnlyList<TrackerIssue>>.Success(mapped);
            }
        }

        return await GetProjectIssuesAsync(project, query, ct);
    }

    public async Task<ProviderResult<IReadOnlyList<TrackerReviewItem>>> GetReviewRequestsAsync(CancellationToken ct = default)
    {
        try
        {
            var (reviews, err) = await githubService.GetReviewRequestsAsync();
            if (err != null)
                return ProviderResult<IReadOnlyList<TrackerReviewItem>>.Failure(err, []);

            var reviewItems = reviews.Select(r => new TrackerReviewItem(
                Id: $"github:{r.Repository}#{r.Number}",
                Key: $"#{r.Number}",
                Title: r.Title,
                Body: r.Body,
                Labels: r.Labels,
                Assignees: r.Assignees,
                Repository: r.Repository,
                Url: r.Url,
                Branch: r.Branch,
                ProviderId: ProviderId,
                UpdatedAt: r.UpdatedAt
            )).ToList();

            return ProviderResult<IReadOnlyList<TrackerReviewItem>>.Success(reviewItems);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get GitHub review requests");
            return ProviderResult<IReadOnlyList<TrackerReviewItem>>.Failure(ex.Message, []);
        }
    }

    private TrackerIssue MapIssue(GitHubIssue issue, string? repository)
    {
        var repo = issue.Repository ?? repository;
        var id = repo != null ? $"github:{repo}#{issue.Number}" : $"github:#{issue.Number}";
        var url = issue.Url ?? (repo != null ? $"https://github.com/{repo}/issues/{issue.Number}" : null);

        return new TrackerIssue(
            Id: id,
            Key: $"#{issue.Number}",
            Title: issue.Title,
            Body: issue.Body,
            Labels: issue.Labels,
            Assignees: issue.Assignees,
            Scope: repo,
            Url: url,
            ProviderId: ProviderId,
            Status: "Open",
            Priority: ExtractPriority(issue.Labels),
            UpdatedAt: issue.UpdatedAt
        );
    }

    private static string? ExtractPriority(string[] labels)
    {
        foreach (var label in labels)
        {
            var lower = label.ToLowerInvariant();
            if (lower is "p0" or "priority:critical" or "urgent") return "Urgent";
            if (lower is "p1" or "priority:high" or "high") return "High";
            if (lower is "p2" or "priority:medium" or "medium") return "Medium";
            if (lower is "p3" or "priority:low" or "low") return "Low";
        }
        return null;
    }
}
