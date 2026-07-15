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
    /// Pipeline stage of the Jobs board. All columns always render, in this order.
    /// </summary>
    internal enum BoardColumn
    {
        Planning,
        Draft,
        Implementing,
        Review
    }

    /// <summary>
    /// Builds the Kanban board as a plan pipeline with four fixed columns:
    /// Planning (jobs creating or revising a draft), Draft (ready drafts with an
    /// actions dropdown), Implementing (plans an ExecutePlan job is working on) and
    /// Review (plans awaiting review, including those with open pull requests).
    /// Dragging a Draft card into Implementing starts execution of that plan.
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

        // Standalone Planning cards for jobs that create/revise a draft. Execute jobs
        // are intentionally excluded here: their plan already renders as an
        // Implementing card below, so listing the job too would duplicate it.
        foreach (var job in activeJobs)
        {
            if (job.TypedArgs is not (CreatePlanArgs or UpdatePlanArgs or SplitPlanArgs or ExpandPlanArgs))
                continue;

            var jobId = job.Id;
            cards.Add(new BoardCard(
                Id: jobId,
                Column: BoardColumn.Planning,
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
                PlanFolder: null,
                DraftPlan: null));
        }

        foreach (var plan in planService.GetPlans())
        {
            activeJobByPlanFolder.TryGetValue(plan.FolderPath, out var activeJob);

            // A create/update/split/expand job already rendered its own Planning card above.
            if (activeJob?.TypedArgs is CreatePlanArgs or UpdatePlanArgs or SplitPlanArgs or ExpandPlanArgs)
                continue;

            if (activeCreatePlanIds.Any(id =>
                    plan.FolderName.StartsWith(id + "-", StringComparison.OrdinalIgnoreCase)))
                continue;

            var executing = activeJob?.TypedArgs is ExecutePlanArgs or RetryPlanArgs
                || plan.Status == PlanStatus.Executing;

            var column = plan.Status switch
            {
                // A plan an ExecutePlan job is working on is briefly Creating before it
                // flips to Executing; keep it in Implementing the whole time so the card
                // doesn't jump to Planning and then vanish.
                _ when executing => BoardColumn.Implementing,
                PlanStatus.Creating or PlanStatus.Updating => BoardColumn.Planning,
                PlanStatus.Draft or PlanStatus.Blocked => BoardColumn.Draft,
                PlanStatus.Review or PlanStatus.Failed => BoardColumn.Review,
                _ => (BoardColumn?)null
            };

            if (column == null)
                continue;

            Action onClick = column switch
            {
                BoardColumn.Planning => () => showPlanSheet(plan.FolderPath),
                BoardColumn.Draft => () => nav.Navigate<DraftsApp>(new DraftsAppArgs(plan.FolderName)),
                BoardColumn.Implementing => activeJob != null
                    ? () => onJobClick(activeJob.Id)
                    : () => showPlanSheet(plan.FolderPath),
                BoardColumn.Review when plan.Prs.Count > 0 => () => nav.Navigate<PullRequestApp>(),
                BoardColumn.Review => () => nav.Navigate<ReviewApp>(new ReviewAppArgs(plan.FolderName)),
                _ => () => showPlanSheet(plan.FolderPath)
            };

            cards.Add(BuildPlanCard(plan, column.Value, projectColors, activeJob, latestJobByPlanId, onClick));
        }

        return cards
            .ToKanban(
                c => c.Column,
                c => c.Id,
                c => c.Order)
            .Columns(BoardColumn.Planning, BoardColumn.Draft, BoardColumn.Implementing, BoardColumn.Review)
            .ColumnHeader(GetColumnHeader)
            .ColumnIcon(GetColumnIcon)
            .ColumnWidth(Size.Units(80))
            .CardOrder(c => c.Order, descending: true)
            .CardBuilder((BoardCard c) =>
                BuildCardWidget(c, jobService, planService, showPlanSheet, showUpdatePlan, showDeletePlan, refresh))
            .OnMove(e => HandleCardMove(e.Value, jobService, planService, refresh));
    }

    /// <summary>
    /// Starts execution when a Draft card is dropped into the Implementing column.
    /// Any other move is a no-op: the board rebuilds from plan/job state, so the card
    /// snaps back to where its state dictates.
    /// </summary>
    private static void HandleCardMove(
        (object? CardId, BoardColumn ToColumn, int? TargetIndex) move,
        IJobService jobService,
        IPlanReaderService planService,
        Action refresh)
    {
        if (move.ToColumn != BoardColumn.Implementing) return;

        var folderName = move.CardId?.ToString();
        if (string.IsNullOrEmpty(folderName)) return;

        var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
        if (plan is null || plan.Status is not (PlanStatus.Draft or PlanStatus.Blocked)) return;

        // JobService.StartJob captures the pre-state and transitions the plan to
        // Creating, then Executing once the job launches.
        jobService.StartJob(new ExecutePlanArgs(plan.FolderPath));
        refresh();
    }

    private static string GetColumnHeader(BoardColumn column) => column switch
    {
        BoardColumn.Planning => "Planning",
        BoardColumn.Draft => "Draft",
        BoardColumn.Implementing => "Implementing",
        BoardColumn.Review => "Review",
        _ => column.ToString()
    };

    private static string GetColumnIcon(BoardColumn column) => column switch
    {
        BoardColumn.Planning => "ScanLine",
        BoardColumn.Draft => "Feather",
        BoardColumn.Implementing => "Hammer",
        BoardColumn.Review => "ThumbsUp",
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
        var (status, statusIcon) = GetPlanCardStatus(plan, column, activeJob);

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
            PlanFolder: plan.FolderPath,
            DraftPlan: column == BoardColumn.Draft ? plan : null);
    }

    /// <summary>
    /// Icon shown in the card's top-left status tile: a spinner while a job is
    /// working on the plan, otherwise a state glyph matching the pipeline stage.
    /// </summary>
    private static (string Icon, bool Spin) GetPlanCardIcon(PlanFile plan, BoardColumn column, JobItem? activeJob)
    {
        if (column is BoardColumn.Planning or BoardColumn.Implementing)
            return ("Loader", activeJob?.Status == JobStatus.Running);

        return plan.Status switch
        {
            PlanStatus.Blocked => ("Hourglass", false),
            PlanStatus.Failed => ("TriangleAlert", false),
            _ when plan.Prs.Count > 0 => ("GitPullRequest", false),
            _ => ("Eye", false)
        };
    }

    private static (string Status, string StatusIcon) GetPlanCardStatus(PlanFile plan, BoardColumn column, JobItem? activeJob)
    {
        if (activeJob != null)
            return (BuildJobStatusLine(activeJob), "CornerDownRight");

        if (column == BoardColumn.Implementing)
            return ("Implementing", "Hammer");

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
    /// Footer metadata: a clickable plan id (opens the plan sheet), plus elapsed/total
    /// job time and token spend — omitting whichever values are unknown for the card.
    /// </summary>
    private static TendrilCardMeta[] BuildCardMeta(string planId, JobItem? job)
    {
        var meta = new List<TendrilCardMeta>();

        if (!string.IsNullOrEmpty(planId))
            meta.Add(new TendrilCardMeta("FileText", planId, Tag: MetaOpenPlanTag));

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

    private const string MetaOpenPlanTag = "OpenPlan";

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
        Action<string> showPlanSheet,
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

        if (!string.IsNullOrEmpty(c.PlanFolder))
        {
            var folder = c.PlanFolder;
            widget = widget.WithOnMetaClick(tag =>
            {
                if (tag == MetaOpenPlanTag)
                    showPlanSheet(folder);
            });
        }

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
    /// Updating/Creating and moves the card back to Planning on the next refresh.
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
        string? PlanFolder,
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
