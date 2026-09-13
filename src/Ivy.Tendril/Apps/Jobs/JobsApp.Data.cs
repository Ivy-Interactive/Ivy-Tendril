using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    private Dictionary<string, string> BuildProjectColorMapping(IConfigService config) =>
        ProjectHelper.BuildColorMapping(config);

    internal static List<JobItemRow> BuildJobRows(List<JobItem> jobs, IPlanReaderService planService)
    {
        return jobs.Select(j =>
        {
            var planId = ExtractPlanId(j.PlanFile);
            if (string.IsNullOrEmpty(planId) && !string.IsNullOrEmpty(j.ReportedPlanId))
                planId = j.ReportedPlanId;

            return new JobItemRow
            {
                Id = j.Id,
                Status = j.Status.ToString(),
                PlanId = planId,
                Prompt = GetPromptDisplay(j, planService),
                Type = j.Type,
                Project = string.Join(", ", ProjectHelper.ParseProjects(j.Project)),
                Timer = FormatTimer(j),
                Timestamp = FormatTimestamp(j),
                Cost = FormatJobCost(j),
                Tokens = j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : "",
                AgentOutput = FormatAgentOutput(j),
                StatusMessage = GetStatusMessage(j),
                ErrorContext = j.Status is JobStatus.Failed or JobStatus.Timeout
                    ? GetErrorContext(j)
                    : null
            };
        })
            .OrderByDescending(r => ExtractJobNumber(r.Id))
            .ToList();
    }

    /// <summary>
    ///     The Cost cell. An estimate derived from tokens times the price list is prefixed with "~" so
    ///     the column never presents a figure nobody was actually charged as a charge; see
    ///     <see cref="JobCostSources.Estimated" />.
    /// </summary>
    internal static string FormatJobCost(JobItem job)
    {
        if (job.Cost is not { } cost) return "";

        var formatted = FormatHelper.FormatCost(cost);
        return job.CostSource == JobCostSources.Estimated ? "~" + formatted : formatted;
    }

    internal static int ExtractJobNumber(string jobId)
    {
        if (string.IsNullOrEmpty(jobId)) return 0;
        if (int.TryParse(jobId, out var num)) return num;
        var parts = jobId.Split('-');
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var n)) return n;
        }
        return 0;
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
}
