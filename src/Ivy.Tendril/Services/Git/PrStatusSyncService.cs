using System.Globalization;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.PullRequest;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Git;

public class PrStatusSyncService : IStartable, IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);
    private const int MaxConcurrentRequests = 4;

    private readonly IPlanDatabaseService _database;
    private readonly IGithubService _githubService;
    private readonly IPlanReaderService _planReader;
    private readonly ILogger<PrStatusSyncService> _logger;
    private readonly SemaphoreSlim _concurrencySemaphore = new(MaxConcurrentRequests);
    private Timer? _timer;
    private int _isRunning;

    public PrStatusSyncService(
        IPlanDatabaseService database,
        IGithubService githubService,
        IPlanReaderService planReader,
        ILogger<PrStatusSyncService> logger)
    {
        _database = database;
        _githubService = githubService;
        _planReader = planReader;
        _logger = logger;
    }

    public void Start()
    {
        _timer = new Timer(_ => _ = RunSyncAsync(), null, TimeSpan.FromSeconds(5), CheckInterval);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _concurrencySemaphore.Dispose();
    }

    public async Task RunSyncAsync()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            _logger.LogDebug("PR status sync already running, skipping this cycle");
            return;
        }

        try
        {
            var prUrls = CollectPrUrlsFromPlans();
            if (prUrls.Count == 0) return;

            var existingStatuses = _database.GetAllPrStatuses();
            var urlsToCheck = new List<string>();

            foreach (var url in prUrls)
            {
                if (existingStatuses.TryGetValue(url, out var info) &&
                    string.Equals(info.Status, "Merged", StringComparison.OrdinalIgnoreCase))
                    continue;

                urlsToCheck.Add(url);
            }

            // Also add any new PRs not yet in the database
            foreach (var url in prUrls)
            {
                if (!existingStatuses.ContainsKey(url) && !urlsToCheck.Contains(url))
                    urlsToCheck.Add(url);
            }

            if (urlsToCheck.Count == 0)
            {
                _logger.LogDebug("All {Total} PRs are merged, nothing to check", prUrls.Count);
                return;
            }

            var grouped = GroupByOwnerRepo(urlsToCheck);
            var now = DateTime.UtcNow;

            foreach (var (ownerRepo, urls) in grouped)
            {
                var parts = ownerRepo.Split('/');
                if (parts.Length != 2) continue;

                try
                {
                    var tasks = urls.Select(async url =>
                    {
                        await _concurrencySemaphore.WaitAsync();
                        try
                        {
                            var (info, error) = await _githubService.GetPrStatusAsync(url);

                            if (error is not null)
                            {
                                _logger.LogWarning("Failed to fetch PR status for {Url}: {Error}", url, error);
                                return;
                            }

                            if (info is not null)
                            {
                                _database.UpsertPrStatus(url, parts[0], parts[1], info.Status, info.Branch, now);
                            }
                        }
                        finally
                        {
                            _concurrencySemaphore.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);

                    _logger.LogDebug("Synced {Count} PR statuses for {Repo}", urls.Count, ownerRepo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch PR statuses for {Repo}", ownerRepo);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PR status sync failed");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    public async Task<bool> SyncPrAsync(string prUrl)
    {
        try
        {
            var repoConfig = GithubService.ParseRepoConfigFromIssueOrPrUrl(prUrl);
            if (repoConfig is null)
            {
                _logger.LogWarning("Could not parse owner/repo from PR URL: {Url}", prUrl);
                return false;
            }

            var (info, error) = await _githubService.GetPrStatusAsync(prUrl);

            if (error is not null)
            {
                _logger.LogWarning("Failed to fetch PR status for {Url}: {Error}", prUrl, error);
                return false;
            }

            if (info is not null)
            {
                var now = DateTime.UtcNow;
                _database.UpsertPrStatus(prUrl, repoConfig.Owner, repoConfig.Name, info.Status, info.Branch, now);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync PR status for {Url}", prUrl);
            return false;
        }
    }

    private List<string> CollectPrUrlsFromPlans()
    {
        var plans = _planReader.GetPlans();
        return plans
            .Where(p => p.Prs.Count > 0)
            .SelectMany(p => p.Prs)
            .Where(PullRequestApp.IsValidUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static Dictionary<string, List<string>> GroupByOwnerRepo(List<string> prUrls)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var url in prUrls)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Trim('/').Split('/');
                if (segments.Length < 2) continue;
                var key = $"{segments[0]}/{segments[1]}";
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    result[key] = list;
                }

                list.Add(url);
            }
            catch (UriFormatException)
            {
                // skip malformed URLs
            }
        }

        return result;
    }
}
