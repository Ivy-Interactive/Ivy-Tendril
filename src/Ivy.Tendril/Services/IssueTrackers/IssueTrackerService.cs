using Ivy.Tendril.Services.IssueTrackers.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.IssueTrackers;

public class IssueTrackerService : IIssueTrackerService
{
    private readonly List<IIssueTrackerProvider> _issueProviders;
    private readonly List<IReviewTrackerProvider> _reviewProviders;
    private readonly IConfigService _config;
    private readonly ILogger<IssueTrackerService> _logger;

    public IssueTrackerService(
        IEnumerable<IIssueTrackerProvider> issueProviders,
        IEnumerable<IReviewTrackerProvider> reviewProviders,
        IConfigService config,
        ILogger<IssueTrackerService> logger)
    {
        _issueProviders = issueProviders.ToList();
        _reviewProviders = reviewProviders.ToList();
        _config = config;
        _logger = logger;
    }

    public IReadOnlyList<IIssueTrackerProvider> IssueProviders => _issueProviders;
    public IReadOnlyList<IReviewTrackerProvider> ReviewProviders => _reviewProviders;

    public IIssueTrackerProvider? GetIssueProvider(string providerId) =>
        _issueProviders.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    public IReviewTrackerProvider? GetReviewProvider(string providerId) =>
        _reviewProviders.FirstOrDefault(p => string.Equals(p.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

    public async Task<(List<TrackerIssue> Issues, List<string> Errors)> GetMyAssignedIssuesAsync(CancellationToken ct = default)
    {
        var allIssues = new List<TrackerIssue>();
        var allErrors = new List<string>();

        var connections = _config.Settings.TrackerConnections;
        var tasks = new List<Task<(string Name, ProviderResult<IReadOnlyList<TrackerIssue>> Result)>>();

        if (connections.Count > 0)
        {
            foreach (var conn in connections)
            {
                var provider = GetIssueProvider(conn.Provider);
                if (provider != null)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var result = await provider.GetMyAssignedIssuesAsync(conn, ct);
                        return (conn.Name, result);
                    }, ct));
                }
            }

            if (!connections.Any(c => c.Provider == "github"))
            {
                var ghProvider = GetIssueProvider("github");
                if (ghProvider != null)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var result = await ghProvider.GetMyAssignedIssuesAsync(ct);
                        return (ghProvider.DisplayName, result);
                    }, ct));
                }
            }
        }
        else
        {
            foreach (var provider in _issueProviders)
            {
                if (await provider.IsConfiguredAsync(ct))
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var result = await provider.GetMyAssignedIssuesAsync(ct);
                        return (provider.DisplayName, result);
                    }, ct));
                }
            }
        }

        var results = await Task.WhenAll(tasks);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (providerName, res) in results)
        {
            if (res.Error != null)
            {
                allErrors.Add($"[{providerName}] {res.Error}");
            }
            if (res.Value.Count > 0)
            {
                foreach (var issue in res.Value)
                {
                    if (seenIds.Add(issue.Id))
                    {
                        allIssues.Add(issue);
                    }
                }
            }
        }

        // Sort descending by UpdatedAt (or title if UpdatedAt is null)
        allIssues.Sort((a, b) =>
        {
            if (a.UpdatedAt.HasValue && b.UpdatedAt.HasValue)
                return b.UpdatedAt.Value.CompareTo(a.UpdatedAt.Value);
            if (a.UpdatedAt.HasValue) return -1;
            if (b.UpdatedAt.HasValue) return 1;
            return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        return (allIssues, allErrors);
    }

    public async Task<(List<TrackerReviewItem> Reviews, List<string> Errors)> GetReviewRequestsAsync(CancellationToken ct = default)
    {
        var allReviews = new List<TrackerReviewItem>();
        var allErrors = new List<string>();

        var configuredProviders = new List<IReviewTrackerProvider>();
        foreach (var provider in _reviewProviders)
        {
            if (await provider.IsConfiguredAsync(ct))
            {
                configuredProviders.Add(provider);
            }
        }

        var tasks = configuredProviders.Select(async provider =>
        {
            var result = await provider.GetReviewRequestsAsync(ct);
            return (provider.DisplayName, result);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (providerName, res) in results)
        {
            if (res.Error != null)
            {
                allErrors.Add($"[{providerName}] {res.Error}");
            }
            if (res.Value.Count > 0)
            {
                allReviews.AddRange(res.Value);
            }
        }

        allReviews.Sort((a, b) =>
        {
            if (a.UpdatedAt.HasValue && b.UpdatedAt.HasValue)
                return b.UpdatedAt.Value.CompareTo(a.UpdatedAt.Value);
            if (a.UpdatedAt.HasValue) return -1;
            if (b.UpdatedAt.HasValue) return 1;
            return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        return (allReviews, allErrors);
    }

    public async Task<(List<TrackerIssue> Issues, List<string> Errors)> GetProjectIssuesAsync(
        ProjectConfig project,
        TrackerIssueQuery query,
        CancellationToken ct = default)
    {
        var trackers = project.IssueTrackers;
        if (trackers.Count == 0 && project.IssueTracker != null)
        {
            trackers = [project.IssueTracker];
        }

        // Auto-detect GitHub from project if no trackers configured
        if (trackers.Count == 0)
        {
            var ghProvider = GetIssueProvider("github");
            if (ghProvider == null)
            {
                return ([], ["GitHub provider is not registered."]);
            }

            var result = await ghProvider.GetProjectIssuesAsync(project, query, ct);
            return (result.Value.ToList(), result.Error != null ? [$"[{ghProvider.DisplayName}] {result.Error}"] : []);
        }

        var tasks = trackers.Select(async tracker =>
        {
            var providerId = tracker.Provider;
            if (string.IsNullOrWhiteSpace(providerId)) providerId = "github";

            var provider = GetIssueProvider(providerId);
            var label = tracker.Name ?? provider?.DisplayName ?? providerId;

            if (provider == null)
            {
                return (label, ProviderResult<IReadOnlyList<TrackerIssue>>.Failure($"Issue tracker provider '{providerId}' is not registered or supported.", []));
            }

            var res = await provider.GetProjectIssuesForTrackerAsync(project, tracker, query, ct);
            return (label, res);
        });

        var results = await Task.WhenAll(tasks);
        var allIssues = new List<TrackerIssue>();
        var allErrors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (label, res) in results)
        {
            if (res.Error != null)
            {
                allErrors.Add($"[{label}] {res.Error}");
            }
            if (res.Value.Count > 0)
            {
                foreach (var issue in res.Value)
                {
                    if (seenIds.Add(issue.Id))
                    {
                        allIssues.Add(issue);
                    }
                }
            }
        }

        allIssues.Sort((a, b) =>
        {
            if (a.UpdatedAt.HasValue && b.UpdatedAt.HasValue)
                return b.UpdatedAt.Value.CompareTo(a.UpdatedAt.Value);
            if (a.UpdatedAt.HasValue) return -1;
            if (b.UpdatedAt.HasValue) return 1;
            return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        return (allIssues, allErrors);
    }
}
