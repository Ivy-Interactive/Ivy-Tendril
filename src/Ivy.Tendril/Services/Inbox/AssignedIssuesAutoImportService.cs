using Ivy;
using Ivy.Tendril.Apps.Inbox;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.Inbox;

public class AssignedIssuesAutoImportService : IStartable, IDisposable
{
    private readonly IConfigService _config;
    private readonly IGithubService _githubService;
    private readonly IPlanReaderService _planReader;
    private readonly IJobService _jobService;
    private readonly ILogger<AssignedIssuesAutoImportService> _logger;
    private readonly IQueryService _queryService;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private Timer? _timer;

    public AssignedIssuesAutoImportService(
        IConfigService config,
        IGithubService githubService,
        IPlanReaderService planReader,
        IJobService jobService,
        ILogger<AssignedIssuesAutoImportService> logger,
        IQueryService queryService)
    {
        _config = config;
        _githubService = githubService;
        _planReader = planReader;
        _jobService = jobService;
        _logger = logger;
        _queryService = queryService;
    }

    public void Start()
    {
        var intervalMinutes = Math.Max(1, _config.Settings.Inbox.CheckIntervalMinutes);
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        _timer = new Timer(_ => _ = RunSyncAsync(), null, TimeSpan.FromSeconds(90), interval);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _syncLock.Dispose();
    }

    public Task TriggerManualCheckAsync()
    {
        return RunSyncAsync(force: true);
    }

    public async Task RunSyncAsync(bool force = false)
    {
        if (Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") == "1")
        {
            _logger.LogDebug("Skipping assigned issues auto-import: node is not master.");
            return;
        }

        if (!force && !_config.Settings.Inbox.AutoAcceptAssignedIssues)
        {
            _logger.LogDebug("Skipping assigned issues auto-import: auto-accept is disabled.");
            return;
        }

        if (!await _syncLock.WaitAsync(0))
        {
            _logger.LogDebug("Assigned issues auto-import is already in progress.");
            return;
        }

        try
        {
            var (issues, error) = await _githubService.GetMyAssignedIssuesAsync();
            if (error != null)
            {
                _logger.LogWarning("Failed to fetch assigned GitHub issues: {Error}", error);
                return;
            }

            _queryService.InvalidateByTag(GithubService.MyIssuesQueryTag);

            if (issues == null || issues.Count == 0)
                return;

            var inboxPath = Path.Combine(_config.TendrilHome, "Inbox");
            if (!Directory.Exists(inboxPath))
                Directory.CreateDirectory(inboxPath);

            var activeJobs = _jobService.GetJobs()
                .Where(j => j.Status is not (JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped))
                .ToList();

            var existingPlans = _planReader.GetPlans();

            foreach (var issue in issues)
            {
                // 1. Check if an inbox file already exists for this issue
                var hasMatchingInboxFile = Directory.GetFiles(inboxPath, $"{issue.Number}-*.md*")
                    .Any(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                              f.EndsWith(".md.processing", StringComparison.OrdinalIgnoreCase));

                if (hasMatchingInboxFile)
                {
                    _logger.LogDebug("Issue #{Number} already exists in Inbox, skipping.", issue.Number);
                    continue;
                }

                // 2. Check if an active job is processing this issue
                var hasActiveJob = activeJobs.Any(j =>
                    j.TypedArgs is CreatePlanArgs cp &&
                    cp.Description != null &&
                    cp.Description.Contains($"#{issue.Number}"));

                if (hasActiveJob)
                {
                    _logger.LogDebug("Issue #{Number} is currently being processed by an active job, skipping.", issue.Number);
                    continue;
                }

                // 3. Check if an existing plan already references this issue URL or issue
                var issueUrl = issue.Url ?? (issue.Repository != null
                    ? $"https://github.com/{issue.Repository}/issues/{issue.Number}"
                    : null);

                var hasExistingPlan = existingPlans.Any(p =>
                {
                    if (p.Metadata == null) return false;

                    if (!string.IsNullOrEmpty(issueUrl))
                    {
                        if (string.Equals(p.Metadata.SourceUrl?.TrimEnd('/'), issueUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                            return true;

                        if (p.Metadata.InitialPrompt != null &&
                            p.Metadata.InitialPrompt.Contains(issueUrl, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }

                    return false;
                });

                if (hasExistingPlan)
                {
                    _logger.LogDebug("Issue #{Number} is already tracked by an existing plan, skipping.", issue.Number);
                    continue;
                }

                // 4. Resolve the target project
                var targetProject = (issue.Repository != null
                    ? _githubService.FindProjectForGithubRepo(issue.Repository)?.Name
                    : null) ?? "Auto";

                // 5. Write new inbox file
                var safeName = InboxApp.SanitizeFileName(issue.Title ?? $"issue-{issue.Number}");
                var fileName = $"{issue.Number}-{safeName}.md";
                var filePath = Path.Combine(inboxPath, fileName);

                var resolvedUrl = issue.Url ?? (issue.Repository != null
                    ? $"https://github.com/{issue.Repository}/issues/{issue.Number}"
                    : "");

                var content = $"""
                               ---
                               project: {targetProject}
                               ---
                               {(string.IsNullOrEmpty(resolvedUrl) ? $"# Issue #{issue.Number}: {issue.Title}" : $"[GitHub Issue #{issue.Number}]({resolvedUrl})")}

                               {issue.Body}
                               """;

                await FileHelper.WriteAllTextAsync(filePath, content);
                _logger.LogInformation("Auto-imported assigned issue #{Number} into Inbox for project {Project}.", issue.Number, targetProject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during assigned issues auto-import.");
        }
        finally
        {
            _syncLock.Release();
        }
    }
}
