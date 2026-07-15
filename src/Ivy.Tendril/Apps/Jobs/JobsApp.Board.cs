using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Apps.Review;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;
using TendrilCardWidget = Ivy.Tendril.Widgets.TendrilCard;

namespace Ivy.Tendril.Apps.Jobs;

public partial class JobsApp
{
    /// <summary>
    /// Pipeline stage of the Jobs board. All four columns always render, in this order.
    /// </summary>
    internal enum BoardColumn
    {
        InProgress,
        Draft,
        Review,
        Pr
    }

    /// <summary>
    /// Builds the Kanban board as a plan pipeline with four fixed columns:
    /// In Progress (jobs creating or revising a draft), Draft (ready drafts with an
    /// actions dropdown), Review (plans being implemented or awaiting review) and
    /// PR (plans with open pull requests).
    /// </summary>
    private object BuildBoard(
        List<JobItem> jobs,
        IPlanReaderService planService,
        IJobService jobService,
        Dictionary<string, string> projectColors,
        INavigator nav,
        Action<string> onJobClick,
        Action<string> showPlanSheet,
        Action<string> showUpdatePlan,
        Action<string> showDeletePlan,
        Action refresh)
    {
        var activeJobs = jobs
            .Where(j => j.Status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running or JobStatus.Blocked)
            .ToList();

        var activeJobByPlanFolder = activeJobs
            .Where(j => j.TypedArgs?.PlanFolder != null)
            .GroupBy(j => j.TypedArgs!.PlanFolder!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var activeCreatePlanIds = activeJobs
            .Where(j => j.TypedArgs is CreatePlanArgs)
            .Select(j => j.ReportedPlanId ?? j.AllocatedPlanId)
            .Where(id => id != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cards = new List<BoardCard>();

        foreach (var job in activeJobs)
        {
            if (job.TypedArgs is not (CreatePlanArgs or UpdatePlanArgs or SplitPlanArgs or ExpandPlanArgs))
                continue;

            var jobId = job.Id;
            cards.Add(new BoardCard(
                Id: jobId,
                Column: BoardColumn.InProgress,
                Order: ExtractJobNumber(jobId),
                Title: GetPromptDisplay(job, planService),
                Badge: string.IsNullOrEmpty(job.Type) ? "Job" : job.Type,
                BadgeIcon: "ScanLine",
                Assignee: BuildAssigneeInitials(job.Project),
                AssigneeColor: BuildAssigneeColor(job.Project, projectColors),
                Footer: BuildBoardFooter(job),
                OnClick: () => onJobClick(jobId),
                DraftPlan: null));
        }

        foreach (var plan in planService.GetPlans())
        {
            activeJobByPlanFolder.TryGetValue(plan.FolderPath, out var activeJob);

            if (activeJob?.TypedArgs is UpdatePlanArgs or SplitPlanArgs or ExpandPlanArgs)
                continue;

            if (activeCreatePlanIds.Any(id =>
                    plan.FolderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)))
                continue;

            var card = plan.Status switch
            {
                PlanStatus.Creating or PlanStatus.Updating =>
                    BuildPlanCard(plan, BoardColumn.InProgress, projectColors, activeJob,
                        () => showPlanSheet(plan.FolderPath)),

                PlanStatus.Draft or PlanStatus.Blocked =>
                    BuildPlanCard(plan, BoardColumn.Draft, projectColors, activeJob,
                        () => nav.Navigate<DraftsApp>(new DraftsAppArgs(plan.FolderName))),

                PlanStatus.Executing =>
                    BuildPlanCard(plan, BoardColumn.Review, projectColors, activeJob,
                        activeJob != null
                            ? () => onJobClick(activeJob.Id)
                            : () => showPlanSheet(plan.FolderPath)),

                PlanStatus.Review or PlanStatus.Failed when plan.Prs.Count > 0 =>
                    BuildPlanCard(plan, BoardColumn.Pr, projectColors, activeJob,
                        () => nav.Navigate<PullRequestApp>()),

                PlanStatus.Review or PlanStatus.Failed =>
                    BuildPlanCard(plan, BoardColumn.Review, projectColors, activeJob,
                        () => nav.Navigate<ReviewApp>(new ReviewAppArgs(plan.FolderName))),

                _ => null
            };

            if (card != null)
                cards.Add(card);
        }

        return cards
            .ToKanban(
                c => c.Column,
                c => c.Id,
                c => c.Order)
            .Columns(BoardColumn.InProgress, BoardColumn.Draft, BoardColumn.Review, BoardColumn.Pr)
            .ColumnHeader(GetColumnHeader)
            .ColumnWidth(Size.Units(80))
            .CardOrder(c => c.Order, descending: true)
            .CardBuilder((BoardCard c) => BuildCardWidget(c, jobService, planService, showUpdatePlan, showDeletePlan, refresh));
    }

    private static string GetColumnHeader(BoardColumn column) => column switch
    {
        BoardColumn.InProgress => "In Progress",
        BoardColumn.Draft => "Draft",
        BoardColumn.Review => "Review",
        BoardColumn.Pr => "PR",
        _ => column.ToString()
    };

    private static BoardCard BuildPlanCard(
        PlanFile plan,
        BoardColumn column,
        Dictionary<string, string> projectColors,
        JobItem? activeJob,
        Action onClick)
    {
        return new BoardCard(
            Id: plan.FolderName,
            Column: column,
            Order: plan.Id,
            Title: string.IsNullOrWhiteSpace(plan.Title) ? plan.FolderName : plan.Title,
            Badge: $"#{plan.Id}",
            BadgeIcon: GetColumnBadgeIcon(column),
            Assignee: BuildAssigneeInitials(plan.Project),
            AssigneeColor: BuildAssigneeColor(plan.Project, projectColors),
            Footer: BuildPlanFooter(plan, activeJob),
            OnClick: onClick,
            DraftPlan: column == BoardColumn.Draft ? plan : null);
    }

    private static string GetColumnBadgeIcon(BoardColumn column) => column switch
    {
        BoardColumn.InProgress => "ScanLine",
        BoardColumn.Draft => "Feather",
        BoardColumn.Review => "ThumbsUp",
        BoardColumn.Pr => "GitPullRequest",
        _ => "ScanLine"
    };

    private static string BuildPlanFooter(PlanFile plan, JobItem? activeJob)
    {
        if (activeJob != null)
        {
            var jobFooter = BuildBoardFooter(activeJob);
            return string.IsNullOrWhiteSpace(jobFooter) ? activeJob.Type : jobFooter;
        }

        if (plan.Prs.Count > 0)
            return plan.Prs.Count == 1
                ? $"PR · {FormatPrReference(plan.Prs[0])}"
                : $"{plan.Prs.Count} pull requests";

        return plan.Status switch
        {
            PlanStatus.Review => "Ready for review",
            PlanStatus.Failed => "Failed",
            PlanStatus.Blocked => "Blocked · waiting for dependencies",
            PlanStatus.Draft => string.IsNullOrWhiteSpace(plan.Level) ? "Draft" : $"Draft · {plan.Level}",
            _ => plan.Status.ToString()
        };
    }

    private static string FormatPrReference(string prUrl)
    {
        var lastSegment = prUrl.TrimEnd('/').Split('/').LastOrDefault();
        return int.TryParse(lastSegment, out var number) ? $"#{number}" : prUrl;
    }

    private static readonly TendrilCardMenuItem[] DraftMenuItems =
    [
        new("Update", "Update", "WandSparkles"),
        new("Split", "Split", "Scissors"),
        new("Expand", "Expand", "UnfoldVertical"),
        new("MarkCompleted", "Mark as Completed", "CircleCheck"),
        new("Delete", "Delete", "Trash", Destructive: true)
    ];

    private static object BuildCardWidget(
        BoardCard c,
        IJobService jobService,
        IPlanReaderService planService,
        Action<string> showUpdatePlan,
        Action<string> showDeletePlan,
        Action refresh)
    {
        var widget = new TendrilCardWidget(c.Title)
            .WithBadge(c.Badge, c.BadgeIcon)
            .WithFooter(c.Footer)
            .WithOnClick(c.OnClick);

        if (!string.IsNullOrEmpty(c.Assignee))
            widget = widget.WithAssignee(c.Assignee, c.AssigneeColor ?? "#6b7280");

        if (c.DraftPlan is { } plan)
        {
            widget = widget
                .WithMenu(DraftMenuItems)
                .WithOnMenuSelect(tag =>
                    HandleDraftAction(tag, plan, jobService, planService, showUpdatePlan, showDeletePlan, refresh));
        }

        return widget;
    }

    /// <summary>
    /// Executes a Draft-column dropdown action. Update opens the instructions dialog;
    /// Split and Expand start their jobs directly, which transitions the plan to
    /// Updating/Creating and moves the card back to In Progress on the next refresh.
    /// </summary>
    private static void HandleDraftAction(
        string tag,
        PlanFile plan,
        IJobService jobService,
        IPlanReaderService planService,
        Action<string> showUpdatePlan,
        Action<string> showDeletePlan,
        Action refresh)
    {
        switch (tag)
        {
            case "Update":
                showUpdatePlan(plan.FolderName);
                break;
            case "Split":
                jobService.StartJob(new SplitPlanArgs(plan.FolderPath));
                refresh();
                break;
            case "Expand":
                jobService.StartJob(new ExpandPlanArgs(plan.FolderPath));
                refresh();
                break;
            case "MarkCompleted":
                planService.TransitionState(plan.FolderName, PlanStatus.Completed);
                refresh();
                break;
            case "Delete":
                showDeletePlan(plan.FolderName);
                break;
        }
    }

    private record BoardCard(
        string Id,
        BoardColumn Column,
        int Order,
        string Title,
        string Badge,
        string BadgeIcon,
        string? Assignee,
        string? AssigneeColor,
        string Footer,
        Action OnClick,
        PlanFile? DraftPlan);

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
