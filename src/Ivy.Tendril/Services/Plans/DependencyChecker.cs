using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.PullRequest;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Services.Plans;

internal class DependencyChecker
{
    private const int PrLookupTimeoutMs = 10000;

    private readonly IPlanReaderService? _planReaderService;

    internal DependencyChecker(IPlanReaderService? planReaderService)
    {
        _planReaderService = planReaderService;
    }

    /// <summary>
    ///     Outcome of one `gh pr view` lookup. The exit code has to travel with the state: gh writes
    ///     its GraphQL error to stderr and nothing to stdout, so a deleted PR, an expired token and a
    ///     rate limit all arrive as an empty state, which the gate used to report as "is '', not
    ///     MERGED".
    /// </summary>
    internal record PrLookup(int ExitCode, string State, string Error);

    /// <summary>
    ///     Seam for the `gh pr view` call, so a test can assert the block reasons without shelling out
    ///     to a real gh and a real network.
    /// </summary>
    internal Func<string, PrLookup> PrStateResolver { get; set; } = RunGhPrView;

    internal (bool Ok, string? BlockReason) CheckDependencies(string planFolder)
    {
        try
        {
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml?.DependsOn == null || planYaml.DependsOn.Count == 0)
                return (true, null);

            var plansDir = _planReaderService?.PlansDirectory;
            if (plansDir == null) return (true, null);

            foreach (var dep in planYaml.DependsOn)
            {
                var result = CheckSingleDependency(dep, plansDir);
                if (!result.Ok) return result;
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Dependency check failed: {ex.Message}");
        }
    }

    private (bool Ok, string? BlockReason) CheckSingleDependency(string dep, string plansDir)
    {
        var depFolder = Path.Combine(plansDir, dep);
        var depPlan = PlanYamlHelper.ReadPlanYaml(depFolder);

        if (depPlan == null)
            return (false, $"Dependency '{dep}' not found");

        if (!depPlan.State.Equals(nameof(PlanStatus.Completed), StringComparison.OrdinalIgnoreCase))
            return (false, $"Dependency '{dep}' is '{depPlan.State}', not Completed");

        foreach (var prUrl in depPlan.Prs.Where(PullRequestApp.IsValidUrl))
        {
            var lookup = PrStateResolver(prUrl);

            // A state gh never reported is not an unmerged PR, it is a lookup that failed. Stay
            // blocked either way (a dependency whose merge cannot be confirmed is not satisfied) but
            // say which of the two it is, so a bad token is not read as an open PR.
            if (lookup.ExitCode != 0 || lookup.State.Length == 0)
                return (false, $"Dependency '{dep}' PR {prUrl} could not be resolved ({DescribeLookupFailure(lookup)})");

            if (!lookup.State.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
                return (false, $"Dependency '{dep}' PR {prUrl} is '{lookup.State}', not MERGED");
        }

        return (true, null);
    }

    /// <summary>
    ///     First line of gh's stderr, which is where it puts the one sentence that explains the
    ///     failure. Truncated because the whole thing has to fit a job StatusMessage.
    /// </summary>
    private static string DescribeLookupFailure(PrLookup lookup)
    {
        var firstLine = lookup.Error
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrEmpty(firstLine))
            return $"gh exited {lookup.ExitCode} with no output";

        const int maxLength = 200;
        var message = firstLine.Length > maxLength ? firstLine[..maxLength] + "..." : firstLine;
        return $"gh: {message}";
    }

    private static PrLookup RunGhPrView(string prUrl)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr view \"{prUrl}\" --json state -q .state",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return new PrLookup(-1, "", "gh could not be started");

            // Drain both pipes concurrently: the error path writes to stderr only, and reading one
            // stream to the end while the other fills its buffer is how this deadlocks.
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            var exited = proc.WaitForExitOrKill(PrLookupTimeoutMs);

            if (!exited)
                return new PrLookup(-1, "", $"gh pr view did not finish within {PrLookupTimeoutMs / 1000}s");

            var drained = Task.WaitAll([stdout, stderr], PrLookupTimeoutMs);
            if (!drained)
                return new PrLookup(-1, "", "gh pr view output could not be read");

            return new PrLookup(proc.ExitCode, stdout.Result.Trim(), stderr.Result.Trim());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to check PR status: {ex.Message}", ex);
        }
    }

    internal void RetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck,
        Action<string>? deleteJob = null,
        Action<JobItem>? persistJob = null)
    {
        var blockedJobs = jobs.Values
            .Where(j => j is { Status: JobStatus.Blocked, TypedArgs: ExecutePlanArgs or RetryPlanArgs })
            .ToList();

        foreach (var blockedJob in blockedJobs)
        {
            var planFolder = blockedJob.TypedArgs?.PlanFolder ?? "";
            if (string.IsNullOrEmpty(planFolder)) continue;

            // A job blocked on sibling jobs (WaitForJobs) is retried by
            // JobCompletionHandler.HandleWaitForJobsDependents when those jobs finish, not here.
            // Restarting it while its WaitForJobs are still pending would immediately re-block it,
            // producing a spurious "Job Unblocked" + "Job Blocked" notification pair (issue #1538).
            if (HasPendingWaitForJobs(blockedJob, jobs))
            {
                var remaining = blockedJob.WaitForJobIds!
                    .Where(id => jobs.TryGetValue(id, out var dep) &&
                                 dep.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
                    .Select(id => jobs[id])
                    .ToList();
                if (remaining.Count > 0)
                {
                    var waitingFor = string.Join(", ", remaining.Select(JobService.DescribeWaitDependency));
                    var newStatus = $"Waiting for {waitingFor}";
                    if (blockedJob.StatusMessage != newStatus)
                    {
                        blockedJob.StatusMessage = newStatus;
                        persistJob?.Invoke(blockedJob);
                    }
                }
                continue;
            }

            var (ok, blockReason) = CheckDependencies(planFolder);
            if (!ok)
            {
                if (!string.IsNullOrEmpty(blockReason) && blockedJob.StatusMessage != blockReason)
                {
                    blockedJob.StatusMessage = blockReason;
                    persistJob?.Invoke(blockedJob);
                }
                continue;
            }

            if (HasActiveJobForPlan(planFolder, jobs)) continue;
            if (!jobs.TryRemove(blockedJob.Id, out _)) continue;
            deleteJob?.Invoke(blockedJob.Id);

            PlanYamlHelper.SetPlanStateByFolder(planFolder, nameof(PlanStatus.Creating));
            startJobSkipDepCheck(blockedJob.TypedArgs!);

            raiseNotification(new JobNotification(
                "Job Unblocked",
                $"{blockedJob.PlanFile}: dependencies now satisfied, auto-restarting",
                true));
        }
    }

    internal void RetryBlockedDependents(
        string completedPlanFolder,
        ConcurrentDictionary<string, JobItem> jobs,
        Func<JobArgsBase, string> startJobSkipDepCheck)
    {
        try
        {
            var completedFolderName = Path.GetFileName(completedPlanFolder);
            var plansDir = _planReaderService?.PlansDirectory;
            if (string.IsNullOrEmpty(plansDir) || !Directory.Exists(plansDir)) return;

            foreach (var dir in Directory.GetDirectories(plansDir))
            {
                if (!ShouldRetryDependentPlan(dir, completedFolderName, jobs))
                    continue;

                var (allMet, _) = CheckDependencies(dir);
                if (allMet)
                {
                    PlanYamlHelper.SetPlanStateByFolder(dir, nameof(PlanStatus.Creating));
                    startJobSkipDepCheck(new ExecutePlanArgs(dir));
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool ShouldRetryDependentPlan(
        string dir,
        string completedFolderName,
        ConcurrentDictionary<string, JobItem> jobs)
    {
        var planYaml = PlanYamlHelper.ReadPlanYaml(dir);
        if (planYaml == null) return false;
        if (!planYaml.State.Equals(nameof(PlanStatus.Blocked), StringComparison.OrdinalIgnoreCase)) return false;
        if (!planYaml.DependsOn.Contains(completedFolderName, StringComparer.OrdinalIgnoreCase)) return false;

        return !jobs.Values.Any(j =>
            j.TypedArgs is ExecutePlanArgs or RetryPlanArgs &&
            j.Status is JobStatus.Blocked or JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.TypedArgs?.PlanFolder != null &&
            j.TypedArgs.PlanFolder.Equals(dir, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPendingWaitForJobs(JobItem job, ConcurrentDictionary<string, JobItem> jobs)
    {
        if (job.WaitForJobIds is not { Count: > 0 })
            return false;

        return job.WaitForJobIds.Any(id =>
            jobs.TryGetValue(id, out var dep) &&
            dep.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked);
    }

    private static bool HasActiveJobForPlan(string planFolder, ConcurrentDictionary<string, JobItem> jobs)
    {
        return jobs.Values.Any(j =>
        {
            if (j.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs)) return false;
            if (j.Status is not (JobStatus.Running or JobStatus.Queued or JobStatus.Pending)) return false;

            var otherFolder = j.TypedArgs?.PlanFolder;
            return otherFolder != null && otherFolder.Equals(planFolder, StringComparison.OrdinalIgnoreCase);
        });
    }
}
