using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    private Dictionary<string, string> BuildProjectColorMapping(IConfigService config)
    {
        return config.Projects
            .Select(p => new { p.Name, Color = config.GetProjectColor(p.Name) })
            .Where(x => x.Color.HasValue)
            .ToDictionary(x => x.Name, x => x.Color!.Value.ToString());
    }

    private List<JobItemRow> BuildJobRows(List<JobItem> jobs, IPlanReaderService planService)
    {
        return jobs.Select(j =>
        {
            var planId = JobsApp.ExtractPlanId(j.PlanFile);
            if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(j.ReportedPlanId))
                planId = j.ReportedPlanId;

            return new JobItemRow
            {
                Id = j.Id,
                Status = JobsApp.FormatStatusBadge(j.Status),
                PlanId = planId,
                Plan = JobsApp.GetPromptDisplay(j, planService),
                Type = j.Type,
                Project = string.Join(", ", ProjectHelper.ParseProjects(j.Project)),
                Timer = JobsApp.FormatTimer(j),
                Cost = j.Cost.HasValue ? $"${j.Cost.Value:F2}" : "",
                Tokens = j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : "",
                AgentOutput = JobsApp.FormatAgentOutput(j),
                LastOutputTimestamp = j.LastOutputAt,
                StatusMessage = JobsApp.GetStatusMessage(j),
                ErrorContext = j.Status is JobStatus.Failed or JobStatus.Timeout
                    ? JobsApp.GetErrorContext(j)
                    : null
            };
        })
            .OrderByDescending(r => ExtractJobNumber(r.Id))
            .ToList();
    }

    private static int ExtractJobNumber(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return 0;
        var parts = jobId.Split('-');
        return parts.Length > 1 && int.TryParse(parts[^1], out var num) ? num : 0;
    }

    private StackedProgress BuildStatusProgress(List<JobItem> jobs, IConfigService config)
    {
        var statusGroups = jobs
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToArray();

        var statusSegments = statusGroups
            .Select(g => new ProgressSegment(
                g.Count,
                JobsApp.GetStatusColor(g.Status),
                g.Status.ToString()
            ))
            .ToArray();

        return new StackedProgress(statusSegments).ShowLabels();
    }

    private static IEnumerable<DataTableCellUpdate> BuildDataTableUpdates(IJobService jobService)
    {
        var currentJobs = jobService.GetJobs();
        return currentJobs
            .Where(j => j.Status == JobStatus.Running ||
                        ((j.Status is JobStatus.Stopped or JobStatus.Failed or JobStatus.Timeout or JobStatus.Completed)
                         && j.CompletedAt.HasValue
                         && DateTime.UtcNow - j.CompletedAt.Value < TimeSpan.FromMinutes(1)))
            .SelectMany(j => new[]
            {
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.Timer), JobsApp.FormatTimer(j)),
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.Cost), j.Cost.HasValue ? $"${j.Cost.Value:F2}" : ""),
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.Tokens), j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : ""),
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.AgentOutput), JobsApp.FormatAgentOutput(j)),
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.Status), JobsApp.FormatStatusBadge(j.Status)),
                new DataTableCellUpdate(j.Id, nameof(JobItemRow.StatusMessage), JobsApp.GetStatusMessage(j))
            });
    }
}
