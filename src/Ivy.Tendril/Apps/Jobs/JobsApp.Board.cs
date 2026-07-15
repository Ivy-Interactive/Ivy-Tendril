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

        // Most recent finished job per plan id, used to show how much time/tokens a
        // draft cost after its job completed (mirrors the card's meta row design).
        var latestJobByPlanId = jobs
            .Where(j => j.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Timeout or JobStatus.Stopped)
            .Select(j => (PlanId: j.ResolvePlanId(), Job: j))
            .Where(x => x.PlanId.Length > 0)
            .GroupBy(x => x.PlanId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Job.CompletedAt ?? x.Job.StartedAt ?? DateTime.MinValue).First().Job,
                StringComparer.OrdinalIgnoreCase);

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
                Icon: "Loader",
                IconSpin: job.Status == JobStatus.Running,
                Project: BuildProjectLabel(job.Project),
                ProjectColor: BuildProjectColor(job.Project, projectColors),
                Status: BuildJobStatusLine(job),
                StatusIcon: "CornerDownRight",
                Meta: BuildCardMeta(job.ResolvePlanId(), job),
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
                    BuildPlanCard(plan, BoardColumn.InProgress, projectColors, activeJob, latestJobByPlanId,
                        () => showPlanSheet(plan.FolderPath)),

                PlanStatus.Draft or PlanStatus.Blocked =>
                    BuildPlanCard(plan, BoardColumn.Draft, projectColors, activeJob, latestJobByPlanId,
                        () => nav.Navigate<DraftsApp>(new DraftsAppArgs(plan.FolderName))),

                PlanStatus.Executing =>
                    BuildPlanCard(plan, BoardColumn.Review, projectColors, activeJob, latestJobByPlanId,
                        activeJob != null
                            ? () => onJobClick(activeJob.Id)
                            : () => showPlanSheet(plan.FolderPath)),

                PlanStatus.Review or PlanStatus.Failed when plan.Prs.Count > 0 =>
                    BuildPlanCard(plan, BoardColumn.Pr, projectColors, activeJob, latestJobByPlanId,
                        () => nav.Navigate<PullRequestApp>()),

                PlanStatus.Review or PlanStatus.Failed =>
                    BuildPlanCard(plan, BoardColumn.Review, projectColors, activeJob, latestJobByPlanId,
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
            .ColumnIcon(GetColumnIcon)
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

    private static string GetColumnIcon(BoardColumn column) => column switch
    {
        BoardColumn.InProgress => "ScanLine",
        BoardColumn.Draft => "Feather",
        BoardColumn.Review => "ThumbsUp",
        BoardColumn.Pr => "GitPullRequest",
        _ => "ScanLine"
    };

    private static BoardCard BuildPlanCard(
        PlanFile plan,
        BoardColumn column,
        Dictionary<string, string> projectColors,
        JobItem? activeJob,
        Dictionary<string, JobItem> latestJobByPlanId,
        Action onClick)
    {
        var (icon, iconSpin) = GetPlanCardIcon(plan, column, activeJob);
        var (status, statusIcon) = GetPlanCardStatus(plan, activeJob);

        latestJobByPlanId.TryGetValue(plan.Id.ToString("D5"), out var latestJob);

        return new BoardCard(
            Id: plan.FolderName,
            Column: column,
            Order: plan.Id,
            Title: string.IsNullOrWhiteSpace(plan.Title) ? plan.FolderName : plan.Title,
            Icon: icon,
            IconSpin: iconSpin,
            Project: BuildProjectLabel(plan.Project),
            ProjectColor: BuildProjectColor(plan.Project, projectColors),
            Status: status,
            StatusIcon: statusIcon,
            Meta: BuildCardMeta(plan.Id.ToString("D5"), activeJob ?? latestJob),
            OnClick: onClick,
            DraftPlan: column == BoardColumn.Draft ? plan : null);
    }

    /// <summary>
    /// Icon shown in the card's top-left status tile: a spinner while a job is
    /// working on the plan, otherwise a state glyph matching the pipeline stage.
    /// </summary>
    private static (string Icon, bool Spin) GetPlanCardIcon(PlanFile plan, BoardColumn column, JobItem? activeJob)
    {
        if (activeJob != null || column == BoardColumn.InProgress)
            return ("Loader", activeJob?.Status == JobStatus.Running);

        return plan.Status switch
        {
            PlanStatus.Blocked => ("Hourglass", false),
            PlanStatus.Failed => ("TriangleAlert", false),
            _ => column == BoardColumn.Pr ? ("GitPullRequest", false) : ("Eye", false)
        };
    }

    private static (string Status, string StatusIcon) GetPlanCardStatus(PlanFile plan, JobItem? activeJob)
    {
        if (activeJob != null)
            return (BuildJobStatusLine(activeJob), "CornerDownRight");

        if (plan.Prs.Count > 0)
            return (plan.Prs.Count == 1
                ? $"PR {FormatPrReference(plan.Prs[0])}"
                : $"{plan.Prs.Count} pull requests", "GitPullRequest");

        return plan.Status switch
        {
            PlanStatus.Review => ("Ready for review", "Eye"),
            PlanStatus.Failed => ("Failed", "TriangleAlert"),
            PlanStatus.Blocked => ("Blocked · waiting for dependencies", "Hourglass"),
            PlanStatus.Draft => ("Awaiting approval", "Eye"),
            _ => (plan.Status.ToString(), "CornerDownRight")
        };
    }

    /// <summary>
    /// Footer metadata: plan id, elapsed/total job time and token spend — omitting
    /// whichever values are unknown for the card.
    /// </summary>
    private static TendrilCardMeta[] BuildCardMeta(string planId, JobItem? job)
    {
        var meta = new List<TendrilCardMeta>();

        if (!string.IsNullOrEmpty(planId))
            meta.Add(new TendrilCardMeta("FileText", planId));

        if (job != null)
        {
            var timer = FormatTimer(job);
            if (timer != "-")
                meta.Add(new TendrilCardMeta("Timer", timer));

            if (job.Tokens is > 0)
                meta.Add(new TendrilCardMeta("Coins", FormatHelper.FormatTokens(job.Tokens.Value)));
        }

        return meta.ToArray();
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
            .WithIcon(c.Icon, c.IconSpin)
            .WithStatus(c.Status, c.StatusIcon)
            .WithMeta(c.Meta)
            .WithOnClick(c.OnClick);

        if (!string.IsNullOrEmpty(c.Project))
            widget = widget.WithProject(c.Project, c.ProjectColor ?? "#6366f1");

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
        string Icon,
        bool IconSpin,
        string? Project,
        string? ProjectColor,
        string Status,
        string StatusIcon,
        TendrilCardMeta[] Meta,
        Action OnClick,
        PlanFile? DraftPlan);

    private static string? BuildProjectLabel(string project)
    {
        var first = ProjectHelper.ParseProjects(project).FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }

    private static string BuildProjectColor(string project, Dictionary<string, string> projectColors)
    {
        var first = ProjectHelper.ParseProjects(project).FirstOrDefault();
        if (!string.IsNullOrEmpty(first) && projectColors.TryGetValue(first, out var color))
            return color;
        return "#6366f1";
    }

    /// <summary>
    /// The card's muted status line for an active job: the live status message when
    /// the job reports one, otherwise the job status name. Time and tokens live in
    /// the card's meta row, not here.
    /// </summary>
    private static string BuildJobStatusLine(JobItem job)
    {
        var message = GetStatusMessage(job);
        return string.IsNullOrWhiteSpace(message) ? job.Status.ToString() : message;
    }
}
