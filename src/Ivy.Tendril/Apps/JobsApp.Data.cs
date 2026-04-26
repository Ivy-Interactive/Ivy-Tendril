using System.Reactive.Linq;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

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
            var planId = ExtractPlanId(j.PlanFile);
            if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(j.ReportedPlanId))
                planId = j.ReportedPlanId;

            return new JobItemRow
            {
                Id = j.Id,
                Status = j.Status,
                PlanId = planId,
                Plan = GetPromptDisplay(j, planService),
                Type = j.Type,
                Project = string.Join(", ", ProjectHelper.ParseProjects(j.Project)),
                Timer = FormatTimer(j),
                Cost = j.Cost.HasValue ? $"${j.Cost.Value:F2}" : "",
                Tokens = j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : "",
                LastOutput = FormatLastOutput(j),
                LastOutputTimestamp = j.LastOutputAt,
                StatusMessage = GetStatusMessage(j),
                ErrorContext = j.Status is JobStatus.Failed or JobStatus.Timeout
                    ? GetErrorContext(j)
                    : null
            };
        })
            .OrderByDescending(r => r.Id)
            .ToList();
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
                GetStatusColor(g.Status),
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
                new DataTableCellUpdate(j.Id, "Timer", FormatTimer(j)),
                new DataTableCellUpdate(j.Id, "Cost", j.Cost.HasValue ? $"${j.Cost.Value:F2}" : ""),
                new DataTableCellUpdate(j.Id, "Tokens", j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : ""),
                new DataTableCellUpdate(j.Id, "LastOutput", FormatLastOutput(j)),
                new DataTableCellUpdate(j.Id, "Status", j.Status),
                new DataTableCellUpdate(j.Id, "StatusMessage", GetStatusMessage(j))
            });
    }
}
