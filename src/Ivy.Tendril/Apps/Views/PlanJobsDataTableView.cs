using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Views;

public class PlanJobsDataTableView(List<JobItem> jobs, Action<string> showDebug) : ViewBase
{
    public override object Build()
    {
        if (jobs.Count == 0)
            return Text.Muted("No jobs for this plan.");

        var rows = jobs
            .OrderByDescending(j => j.StartedAt ?? DateTime.MinValue)
            .Select(j => new PlanJobRow
            {
                Id = j.Id,
                Status = j.Status == JobStatus.Running
                    ? AnimatedStatusValue.Running(j.Status.ToString())
                    : AnimatedStatusValue.Idle(j.Status.ToString()),
                Type = j.Type,
                Cost = j.Cost.HasValue ? $"${j.Cost.Value:F2}" : "",
                Tokens = j.Tokens.HasValue ? FormatHelper.FormatTokens(j.Tokens.Value) : "",
                StatusMessage = j.StatusMessage ?? ""
            })
            .ToList();

        return rows.AsQueryable()
            .ToDataTable(t => t.Id)
            .Width(Size.Full())
            .Header(t => t.Status, "Status")
            .Header(t => t.Type, "Type")
            .Header(t => t.Cost, "Cost")
            .Header(t => t.Tokens, "Tokens")
            .Header(t => t.StatusMessage, "Status")
            .Width(t => t.Status, Size.Px(110))
            .Width(t => t.Type, Size.Px(110))
            .Width(t => t.Cost, Size.Px(80))
            .Width(t => t.Tokens, Size.Px(80))
            .Width(t => t.StatusMessage, Size.Auto())
            .Renderer(t => t.Status, new AnimatedStatusLabelDisplayRenderer
            {
                Mode = AnimatedStatusMode.Badge,
                BadgeColorMapping = Constants.JobStatusColors.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.Type, new LabelsDisplayRenderer
            {
                BadgeColorMapping = Constants.JobTypeColors.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString()
                )
            })
            .Renderer(t => t.StatusMessage, new TextDisplayRenderer())
            .Hidden(t => t.Id)
            .Config(c =>
            {
                c.AllowSorting = false;
                c.AllowFiltering = false;
                c.ShowSearch = false;
                c.SelectionMode = SelectionModes.None;
                c.ShowIndexColumn = false;
            })
            .RowActions(_ => new[]
            {
                new MenuItem("Debug", Icon: Icons.Bug, Tag: "debug-job")
                    .Tooltip("Show debug details for this job")
            })
            .OnRowAction(e =>
            {
                var id = e.Value.Id?.ToString();
                if (!string.IsNullOrEmpty(id))
                    showDebug(id);
                return ValueTask.CompletedTask;
            });
    }

    private record PlanJobRow
    {
        public string Id { get; init; } = "";
        public string Status { get; init; } = "";
        public string Type { get; init; } = "";
        public string Cost { get; init; } = "";
        public string Tokens { get; init; } = "";
        public string StatusMessage { get; init; } = "";
    }
}
