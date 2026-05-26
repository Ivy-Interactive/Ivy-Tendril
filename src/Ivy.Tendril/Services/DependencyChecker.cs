using System.Collections.Concurrent;
using System.Diagnostics;
using Ivy.Helpers;
using Ivy.Tendril.Apps;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services;

internal class DependencyChecker
{
    private readonly IPlanReaderService? _planReaderService;

    internal DependencyChecker(IPlanReaderService? planReaderService)
    {
        _planReaderService = planReaderService;
    }

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

    private static (bool Ok, string? BlockReason) CheckSingleDependency(string dep, string plansDir)
    {
        var depFolder = Path.Combine(plansDir, dep);
        var depPlan = PlanYamlHelper.ReadPlanYaml(depFolder);

        if (depPlan == null)
            return (false, $"Dependency '{dep}' not found");

        if (!depPlan.State.Equals(nameof(PlanStatus.Completed), StringComparison.OrdinalIgnoreCase))
            return (false, $"Dependency '{dep}' is '{depPlan.State}', not Completed");

        foreach (var prUrl in depPlan.Prs.Where(PullRequestApp.IsValidUrl))
        {
            var prState = GetPrState(prUrl);
            if (prState != null && !prState.Equals("MERGED", StringComparison.OrdinalIgnoreCase))
                return (false, $"Dependency '{dep}' PR {prUrl} is '{prState}', not MERGED");
        }

        return (true, null);
    }

    private static string? GetPrState(string prUrl)
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
            var output = proc?.StandardOutput.ReadToEnd().Trim() ?? "";
            proc.WaitForExitOrKill(10000);
            return output;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to check PR status: {ex.Message}", ex);
        }
    }

    internal void RetryBlockedJobs(
        ConcurrentDictionary<string, JobItem> jobs,
        Action<JobNotification> raiseNotification,
        Func<JobArgsBase, string> startJobSkipDepCheck)
    {
        var blockedJobs = jobs.Values
            .Where(j => j is { Status: JobStatus.Blocked, TypedArgs: ExecutePlanArgs or RetryPlanArgs })
            .ToList();

        foreach (var blockedJob in blockedJobs)
        {
            var planFolder = blockedJob.TypedArgs?.PlanFolder ?? "";
            if (string.IsNullOrEmpty(planFolder)) continue;

            var (ok, _) = CheckDependencies(planFolder);
            if (!ok) continue;

            if (HasActiveJobForPlan(planFolder, jobs)) continue;
            if (!jobs.TryRemove(blockedJob.Id, out _)) continue;

            PlanYamlHelper.SetPlanStateByFolder(planFolder, nameof(PlanStatus.Building));
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
                    PlanYamlHelper.SetPlanStateByFolder(dir, nameof(PlanStatus.Building));
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

    private static bool HasActiveJobForPlan(string planFolder, ConcurrentDictionary<string, JobItem> jobs)
    {
        var planRepos = PlanYamlHelper.ReadPlanYaml(planFolder)?.Repos;

        return jobs.Values.Any(j =>
        {
            if (j.TypedArgs is not (ExecutePlanArgs or RetryPlanArgs)) return false;
            if (j.Status is not (JobStatus.Running or JobStatus.Queued or JobStatus.Pending)) return false;

            var otherFolder = j.TypedArgs?.PlanFolder;
            if (otherFolder == null) return false;

            if (otherFolder.Equals(planFolder, StringComparison.OrdinalIgnoreCase))
                return true;

            if (planRepos is not { Count: > 0 }) return false;
            var otherRepos = PlanYamlHelper.ReadPlanYaml(otherFolder)?.Repos;
            return otherRepos != null && planRepos.Any(r => otherRepos.Contains(r, StringComparer.OrdinalIgnoreCase));
        });
    }
}
