using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Apps.Jobs.Helpers;

public record BlockingDependency
{
    public string? JobId { get; init; }
    public string? JobType { get; init; }
    public JobStatus? JobStatus { get; init; }
    public string? PlanId { get; init; }
    public string? PlanFolder { get; init; }
    public string? PlanStatus { get; init; }
    public string? Title { get; init; }
}

public static class JobDependencyHelper
{
    public static IReadOnlyList<BlockingDependency> GetBlockingDependencies(
        JobItem job,
        IJobService? jobService,
        IPlanReaderService? planService)
    {
        var results = new List<BlockingDependency>();

        // 1. Explicit job dependencies (WaitForJobIds)
        if (job.WaitForJobIds is { Count: > 0 })
        {
            var allJobs = jobService?.GetJobs() ?? [];
            foreach (var waitForId in job.WaitForJobIds)
            {
                var depJob = allJobs.FirstOrDefault(j => j.Id == waitForId);
                if (depJob != null)
                {
                    var depPlanId = depJob.ResolvePlanId();
                    PlanFile? depPlan = null;
                    if (!string.IsNullOrEmpty(depPlanId) && planService != null)
                    {
                        depPlan = planService.GetPlans().FirstOrDefault(p =>
                            p.Id.ToString("D5") == depPlanId ||
                            p.FolderName.StartsWith(depPlanId, StringComparison.OrdinalIgnoreCase));
                    }

                    var title = depPlan?.Title ?? JobsApp.GetFullPrompt(depJob, planService);
                    results.Add(new BlockingDependency
                    {
                        JobId = depJob.Id,
                        JobType = depJob.Type,
                        JobStatus = depJob.Status,
                        PlanId = string.IsNullOrEmpty(depPlanId) ? null : depPlanId,
                        PlanFolder = depPlan?.FolderName ?? (!string.IsNullOrEmpty(depJob.PlanFile) ? depJob.PlanFile : null),
                        PlanStatus = depPlan?.Status.ToString(),
                        Title = title
                    });
                }
                else
                {
                    results.Add(new BlockingDependency
                    {
                        JobId = waitForId,
                        Title = $"Job {waitForId}"
                    });
                }
            }
        }

        // 2. Plan dependencies (DependsOn from plan.yaml)
        string? planFolder = null;
        if (job.TypedArgs is ExecutePlanArgs execArgs)
        {
            planFolder = execArgs.PlanFolder;
        }
        else if (job.TypedArgs is RetryPlanArgs retryArgs)
        {
            planFolder = retryArgs.PlanFolder;
        }
        else if (!string.IsNullOrEmpty(job.PlanFile) && planService?.PlansDirectory != null)
        {
            var candidate = Path.IsPathRooted(job.PlanFile)
                ? job.PlanFile
                : Path.Combine(planService.PlansDirectory, job.PlanFile);
            if (Directory.Exists(candidate))
            {
                planFolder = candidate;
            }
        }

        if (!string.IsNullOrEmpty(planFolder))
        {
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (planYaml?.DependsOn is { Count: > 0 })
            {
                var allJobs = jobService?.GetJobs() ?? [];
                var allPlans = planService?.GetPlans() ?? [];

                foreach (var dep in planYaml.DependsOn)
                {
                    var depPlanId = PlanYamlHelper.ExtractPlanIdFromFolder(dep) ??
                                    (int.TryParse(dep, out var n) ? $"{n:D5}" : dep);

                    var depPlan = allPlans.FirstOrDefault(p =>
                        p.FolderName.Equals(dep, StringComparison.OrdinalIgnoreCase) ||
                        p.FolderName.StartsWith(depPlanId + "-", StringComparison.OrdinalIgnoreCase) ||
                        p.Id.ToString("D5") == depPlanId);

                    PlanYaml? depPlanYaml = null;
                    if (depPlan == null && planService?.PlansDirectory != null)
                    {
                        var depPath = Path.IsPathRooted(dep)
                            ? dep
                            : Path.Combine(planService.PlansDirectory, dep);
                        if (Directory.Exists(depPath))
                        {
                            depPlanYaml = PlanYamlHelper.ReadPlanYaml(depPath);
                        }
                    }

                    var matchingJobs = allJobs
                        .Where(j => j.ResolvePlanId() == depPlanId ||
                                    JobsApp.ExtractPlanId(j.PlanFile) == depPlanId ||
                                    j.ReportedPlanId == depPlanId)
                        .ToList();

                    var depJob = matchingJobs
                        .OrderByDescending(j => j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending or JobStatus.Blocked)
                        .ThenByDescending(j => j.StartedAt)
                        .FirstOrDefault();

                    var planState = depPlan?.Status.ToString() ?? depPlanYaml?.State;
                    var planTitle = depPlan?.Title ?? depPlanYaml?.Title;

                    if (depJob != null)
                    {
                        results.Add(new BlockingDependency
                        {
                            JobId = depJob.Id,
                            JobType = depJob.Type,
                            JobStatus = depJob.Status,
                            PlanId = depPlanId,
                            PlanFolder = depPlan?.FolderName ?? dep,
                            PlanStatus = planState,
                            Title = planTitle ?? JobsApp.GetFullPrompt(depJob, planService)
                        });
                    }
                    else
                    {
                        results.Add(new BlockingDependency
                        {
                            PlanId = depPlanId,
                            PlanFolder = depPlan?.FolderName ?? dep,
                            PlanStatus = planState,
                            Title = planTitle ?? dep
                        });
                    }
                }
            }
        }

        // 3. Fallback: Parse patterns from StatusMessage if no explicit dependencies resolved
        if (results.Count == 0 && !string.IsNullOrEmpty(job.StatusMessage))
        {
            var allJobs = jobService?.GetJobs() ?? [];

            // Pattern A: "(job 00045)"
            var jobMatches = Regex.Matches(job.StatusMessage, @"\(job\s+([^\)]+)\)");
            foreach (Match match in jobMatches)
            {
                var jobId = match.Groups[1].Value.Trim();
                var depJob = allJobs.FirstOrDefault(j => j.Id == jobId);
                results.Add(new BlockingDependency
                {
                    JobId = jobId,
                    JobType = depJob?.Type,
                    JobStatus = depJob?.Status,
                    PlanId = depJob?.ResolvePlanId(),
                    Title = depJob != null ? JobsApp.GetFullPrompt(depJob, planService) : $"Job {jobId}"
                });
            }

            // Pattern B: "Blocked job 00045 failed"
            var blockedJobMatch = Regex.Match(job.StatusMessage, @"[Bb]locked job\s+(\S+)\s+failed");
            if (blockedJobMatch.Success)
            {
                var jobId = blockedJobMatch.Groups[1].Value.Trim();
                var depJob = allJobs.FirstOrDefault(j => j.Id == jobId);
                results.Add(new BlockingDependency
                {
                    JobId = jobId,
                    JobType = depJob?.Type,
                    JobStatus = depJob?.Status,
                    PlanId = depJob?.ResolvePlanId(),
                    Title = depJob != null ? JobsApp.GetFullPrompt(depJob, planService) : $"Job {jobId}"
                });
            }

            // Pattern C: "Dependency '00015-SomePlan'..."
            var depMatches = Regex.Matches(job.StatusMessage, @"Dependency\s+'([^']+)'");
            foreach (Match match in depMatches)
            {
                var dep = match.Groups[1].Value.Trim();
                var depPlanId = PlanYamlHelper.ExtractPlanIdFromFolder(dep) ?? dep;
                var depJob = allJobs.FirstOrDefault(j =>
                    j.ResolvePlanId() == depPlanId ||
                    JobsApp.ExtractPlanId(j.PlanFile) == depPlanId ||
                    j.ReportedPlanId == depPlanId);

                results.Add(new BlockingDependency
                {
                    JobId = depJob?.Id,
                    JobType = depJob?.Type,
                    JobStatus = depJob?.Status,
                    PlanId = depPlanId,
                    PlanFolder = dep,
                    Title = dep
                });
            }
        }

        // Deduplicate
        return results
            .GroupBy(r => !string.IsNullOrEmpty(r.JobId) ? $"job:{r.JobId}" : $"plan:{r.PlanId ?? r.PlanFolder}")
            .Select(g => g.First())
            .ToList();
    }
}
