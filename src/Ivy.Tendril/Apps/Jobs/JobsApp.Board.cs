using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using TendrilCardWidget = Ivy.Tendril.Widgets.TendrilCard;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    /// <summary>
    /// Builds a Kanban board of the current jobs, grouped by <see cref="JobStatus"/> and
    /// rendered with the custom <see cref="TendrilCardWidget"/> card design. Columns follow
    /// the natural status order; clicking a card opens its output sheet.
    /// </summary>
    private object BuildBoard(
        List<JobItem> jobs,
        IPlanReaderService planService,
        Dictionary<string, string> projectColors,
        Action<string> onCardClick)
    {
        if (jobs.Count == 0)
            return new Fragment();

        var cards = jobs
            .Select(j => new BoardCard(
                Id: j.Id,
                Status: j.Status,
                Order: ExtractJobNumber(j.Id),
                Title: GetPromptDisplay(j, planService),
                Badge: string.IsNullOrEmpty(j.Type) ? "Job" : j.Type,
                Assignee: BuildAssigneeInitials(j.Project),
                AssigneeColor: BuildAssigneeColor(j.Project, projectColors),
                Footer: BuildBoardFooter(j)))
            .ToList();

        return cards
            .ToKanban(
                c => c.Status,
                c => c.Id,
                c => c.Order)
            .ColumnWidth(Size.Units(80))
            .CardOrder(c => c.Order, descending: true)
            .ColumnHeader(status => status.ToString())
            .CardBuilder((BoardCard c) => (object)new TendrilCardWidget(c.Title)
                .WithBadge(c.Badge, "ScanLine")
                .WithAssignee(c.Assignee, c.AssigneeColor)
                .WithFooter(c.Footer)
                .WithOnClick(() => onCardClick(c.Id)));
    }

    private record BoardCard(
        string Id,
        JobStatus Status,
        int Order,
        string Title,
        string Badge,
        string Assignee,
        string AssigneeColor,
        string Footer);

    private static string BuildAssigneeInitials(string project)
    {
        var names = ProjectHelper.ParseProjects(project);
        var first = names.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
            return "—";

        var letters = first
            .Where(char.IsLetterOrDigit)
            .Take(2)
            .ToArray();
        return letters.Length > 0 ? new string(letters).ToUpperInvariant() : "—";
    }

    private static string BuildAssigneeColor(string project, Dictionary<string, string> projectColors)
    {
        var first = ProjectHelper.ParseProjects(project).FirstOrDefault();
        if (!string.IsNullOrEmpty(first) && projectColors.TryGetValue(first, out var color))
            return color;
        return "#6b7280";
    }

    private static string BuildBoardFooter(JobItem job)
    {
        var message = GetStatusMessage(job);
        if (!string.IsNullOrWhiteSpace(message))
            return message;

        var timer = FormatTimer(job);
        return timer == "-" ? job.Status.ToString() : $"{job.Status} · {timer}";
    }
}
