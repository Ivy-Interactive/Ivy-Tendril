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
        Review,
        Pr
    }

    /// <summary>
    /// Callbacks the board's cards and drag-moves invoke on the hosting app: sheet and
    /// dialog triggers keyed by job id or plan folder name, plus a full refresh.
    /// </summary>
    internal sealed record BoardActions(
        Action<string> OnJobClick,
        Action<string> ShowPlanSheet,
        Action<string> ShowUpdatePlan,
        Action<string> ShowDeletePlan,
        Action<string> ShowDebugJob,
        Action<string> ShowRerunJob,
        Action<string> ShowDeleteJob,
        Action<string> ShowSuggestChanges,
        Action<string> ShowCreatePr,
        Action<string> ShowResetToDraft,
        Action<string> ShowDiscardPlan,
        Action<string?> SetSelected,
        Action Refresh);

    /// <summary>
    /// Builds the Kanban board as a plan pipeline with five fixed columns:
    /// Planning (jobs creating or revising a draft), Draft (ready drafts with an
    /// actions dropdown), Implementing (plans an ExecutePlan job is working on),
    /// Review (plans awaiting review) and PR (reviewed plans with an open pull
    /// request — the board's done column). Drag transitions map to plan actions:
    /// Draft→Implementing starts execution, Draft→Planning opens the update dialog,
    /// Implementing→Draft stops the running job, Review→Implementing opens the
    /// request-changes dialog.
    /// </summary>
    private object BuildBoard(
        List<JobItem> jobs,
        IPlanReaderService planService,
        IJobService jobService,
        Dictionary<string, string> projectColors,
        INavigator nav,
        string? selectedCardId,
        BoardActions actions)
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
                TimerStartedAt: GetLiveTimerStart(job),
                OnClick: () => actions.OnJobClick(jobId),
                PlanFolder: null,
                Plan: null,
                MenuJob: job));
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
                PlanStatus.Review when plan.Prs.Count > 0 => BoardColumn.Pr,
                PlanStatus.Review or PlanStatus.Failed => BoardColumn.Review,
                _ => (BoardColumn?)null
            };

            if (column == null)
                continue;

            Action onClick = column switch
            {
                BoardColumn.Planning => () => actions.ShowPlanSheet(plan.FolderPath),
                BoardColumn.Draft => () => actions.ShowPlanSheet(plan.FolderPath),
                BoardColumn.Implementing => activeJob != null
                    ? () => actions.OnJobClick(activeJob.Id)
                    : () => actions.ShowPlanSheet(plan.FolderPath),
                BoardColumn.Pr => () => nav.Navigate<PullRequestApp>(),
                BoardColumn.Review => () => nav.Navigate<ReviewApp>(new ReviewAppArgs(plan.FolderName)),
                _ => () => actions.ShowPlanSheet(plan.FolderPath)
            };

            cards.Add(BuildPlanCard(plan, column.Value, projectColors, activeJob, latestJobByPlanId, onClick));
        }

        return cards
            .ToKanban(
                c => c.Column,
                c => c.Id,
                c => c.Order)
            .Columns(BoardColumn.Planning, BoardColumn.Draft, BoardColumn.Implementing, BoardColumn.Review, BoardColumn.Pr)
            .ColumnHeader(GetColumnHeader)
            .ColumnIcon(GetColumnIcon)
            .CardOrder(c => c.Order, descending: true)
            .CardBuilder((BoardCard c) =>
                BuildCardWidget(c, jobService, planService, selectedCardId, actions))
            .OnMove(e => HandleCardMove(e.Value, jobService, planService, actions));
    }

    /// <summary>
    /// Maps a card drop onto a plan action. Anything not listed is a no-op: the board
    /// rebuilds from plan/job state, so the card snaps back to where its state dictates.
    /// </summary>
    private static void HandleCardMove(
        (object? CardId, BoardColumn ToColumn, int? TargetIndex) move,
        IJobService jobService,
        IPlanReaderService planService,
        BoardActions actions)
    {
        var folderName = move.CardId?.ToString();
        if (string.IsNullOrEmpty(folderName)) return;

        // Standalone Planning job cards use a job id as their card id and have no
        // plan folder to act on; their drags are ignored.
        var plan = planService.GetPlans().FirstOrDefault(p => p.FolderName == folderName);
        if (plan is null) return;

        switch (move.ToColumn)
        {
            // Draft → Implementing: start executing the plan. JobService.StartJob
            // captures the pre-state and transitions the plan to Creating, then
            // Executing once the job launches.
            case BoardColumn.Implementing when plan.Status is PlanStatus.Draft or PlanStatus.Blocked:
                jobService.StartJob(new ExecutePlanArgs(plan.FolderPath));
                actions.Refresh();
                break;

            // Review → Implementing: request changes so the agent re-implements.
            case BoardColumn.Implementing when plan.Status is PlanStatus.Review or PlanStatus.Failed:
                actions.ShowSuggestChanges(folderName);
                break;

            // Draft → Planning: revise the draft via the update-instructions dialog.
            case BoardColumn.Planning when plan.Status is PlanStatus.Draft or PlanStatus.Blocked:
                actions.ShowUpdatePlan(folderName);
                break;

            // Implementing → Draft: stop the running job; StopJob reverts the plan to
            // its captured pre-job state (Draft). Falls back to a direct state
            // transition for a stale Executing plan with no live job.
            case BoardColumn.Draft:
                StopActiveJob(plan, jobService, planService, actions.Refresh);
                break;
        }
    }

    private static void StopActiveJob(
        PlanFile plan,
        IJobService jobService,
        IPlanReaderService planService,
        Action refresh)
    {
        var activeJob = jobService.GetJobs().FirstOrDefault(j =>
            j.Status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running or JobStatus.Blocked &&
            j.TypedArgs?.PlanFolder != null &&
            string.Equals(j.TypedArgs.PlanFolder, plan.FolderPath, StringComparison.OrdinalIgnoreCase));

        if (activeJob != null)
        {
            jobService.StopJob(activeJob.Id);
            refresh();
            return;
        }

        if (plan.Status is PlanStatus.Executing or PlanStatus.Creating)
        {
            planService.TransitionState(plan.FolderName, PlanStatus.Draft);
            refresh();
        }
    }

    private static string GetColumnHeader(BoardColumn column) => column switch
    {
        BoardColumn.Planning => "Planning",
        BoardColumn.Draft => "Draft",
        BoardColumn.Implementing => "Implementing",
        BoardColumn.Review => "Review",
        BoardColumn.Pr => "PR",
        _ => column.ToString()
    };

    private static string GetColumnIcon(BoardColumn column) => column switch
    {
        BoardColumn.Planning => "ScanLine",
        BoardColumn.Draft => "Feather",
        BoardColumn.Implementing => "Hammer",
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
            TimerStartedAt: GetLiveTimerStart(activeJob),
            OnClick: onClick,
            PlanFolder: plan.FolderPath,
            Plan: plan,
            // Menu actions fall back to the plan's latest finished job so stale
            // Planning/Implementing cards (no live job) can still be debugged,
            // restarted or deleted.
            MenuJob: activeJob ?? latestJob);
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
    /// Footer metadata: a clickable plan id (opens the plan sheet), plus total job
    /// time and token spend — omitting whichever values are unknown for the card.
    /// A running job's elapsed time is intentionally not included here: it renders
    /// as the card's live client-side timer instead (see <see cref="GetLiveTimerStart"/>).
    /// </summary>
    private static TendrilCardMeta[] BuildCardMeta(string planId, JobItem? job)
    {
        var meta = new List<TendrilCardMeta>();

        if (!string.IsNullOrEmpty(planId))
            meta.Add(new TendrilCardMeta("FileText", planId, Tag: MetaOpenPlanTag));

        if (job != null)
        {
            if (job.Status != JobStatus.Running)
            {
                var timer = FormatTimer(job);
                if (timer != "-")
                    meta.Add(new TendrilCardMeta("Timer", timer));
            }

            if (job.Tokens is > 0)
                meta.Add(new TendrilCardMeta("Coins", FormatHelper.FormatTokens(job.Tokens.Value)));
        }

        return meta.ToArray();
    }

    /// <summary>Start timestamp for the card's live ticking timer; null unless the job is running.</summary>
    private static DateTime? GetLiveTimerStart(JobItem? job) =>
        job is { Status: JobStatus.Running, StartedAt: not null } ? job.StartedAt : null;

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

    private static readonly TendrilCardMenuItem[] JobMenuItems =
    [
        new("Stop", "Stop", "Pause"),
        new("Debug", "Debug", "Bug"),
        new("Restart", "Restart", "RotateCw"),
        new("Delete", "Delete", "Trash", Destructive: true)
    ];

    private static readonly TendrilCardMenuItem[] DeleteOnlyMenuItems =
    [
        new("Delete", "Delete", "Trash", Destructive: true)
    ];

    // Mirrors the plan actions ReviewApp offers (see ContentView.BuildActionBar).
    private static readonly TendrilCardMenuItem[] ReviewMenuItems =
    [
        new("CreatePr", "Create PR", "GitPullRequest"),
        new("ResetToDraft", "Reset to Draft", "RotateCcw"),
        new("RequestChanges", "Request Changes", "MessageSquare"),
        new("SetCompleted", "Set Completed", "CircleCheck"),
        new("Discard", "Discard", "Trash", Destructive: true)
    ];

    private static object BuildCardWidget(
        BoardCard c,
        IJobService jobService,
        IPlanReaderService planService,
        string? selectedCardId,
        BoardActions actions)
    {
        // Wrap the card's click so opening its sheet also marks it selected.
        Action onClick = () =>
        {
            actions.SetSelected(c.Id);
            c.OnClick();
        };

        var widget = new TendrilCardWidget(c.Title)
            .WithIcon(c.Icon, c.IconSpin)
            .WithStatus(c.Status, c.StatusIcon)
            .WithMeta(c.Meta)
            .WithTimerStartedAt(c.TimerStartedAt)
            .WithSelected(c.Id == selectedCardId)
            .WithOnClick(onClick);

        if (!string.IsNullOrEmpty(c.Project))
            widget = widget.WithProject(c.Project, c.ProjectColor ?? "#6366f1");

        if (!string.IsNullOrEmpty(c.PlanFolder))
        {
            var folder = c.PlanFolder;
            var cardId = c.Id;
            widget = widget.WithOnMetaClick(tag =>
            {
                if (tag == MetaOpenPlanTag)
                {
                    actions.SetSelected(cardId);
                    actions.ShowPlanSheet(folder);
                }
            });
        }

        switch (c.Column)
        {
            case BoardColumn.Draft when c.Plan is { } draftPlan:
                widget = widget
                    .WithMenu(DraftMenuItems)
                    .WithOnMenuSelect(tag =>
                        HandleDraftAction(tag, draftPlan, jobService, planService, actions));
                break;

            case BoardColumn.Planning or BoardColumn.Implementing when c.MenuJob is { } job:
                widget = widget
                    .WithMenu(JobMenuItems)
                    .WithOnMenuSelect(tag => HandleJobAction(tag, job, jobService, actions));
                break;

            // A Planning plan card with no backing job (e.g. a plan stuck Creating)
            // still needs to be deletable.
            case BoardColumn.Planning when c.Plan is { } planningPlan:
                widget = widget
                    .WithMenu(DeleteOnlyMenuItems)
                    .WithOnMenuSelect(_ => actions.ShowDeletePlan(planningPlan.FolderName));
                break;

            case BoardColumn.Review or BoardColumn.Pr when c.Plan is { } reviewPlan:
                widget = widget
                    .WithMenu(ReviewMenuItems)
                    .WithOnMenuSelect(tag => HandleReviewAction(tag, reviewPlan, planService, actions));
                break;
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
        BoardActions actions)
    {
        switch (tag)
        {
            case "Update":
                actions.ShowUpdatePlan(plan.FolderName);
                break;
            case "Split":
                jobService.StartJob(new SplitPlanArgs(plan.FolderPath));
                actions.Refresh();
                break;
            case "Expand":
                jobService.StartJob(new ExpandPlanArgs(plan.FolderPath));
                actions.Refresh();
                break;
            case "MarkCompleted":
                planService.TransitionState(plan.FolderName, PlanStatus.Completed);
                actions.Refresh();
                break;
            case "Delete":
                actions.ShowDeletePlan(plan.FolderName);
                break;
        }
    }

    /// <summary>
    /// Executes a Planning/Implementing-column dropdown action against the card's
    /// active job. Restart stops the job first, then opens the rerun dialog so the
    /// user can optionally add corrective feedback.
    /// </summary>
    private static void HandleJobAction(
        string tag,
        JobItem job,
        IJobService jobService,
        BoardActions actions)
    {
        switch (tag)
        {
            case "Stop":
                jobService.StopJob(job.Id);
                actions.Refresh();
                break;
            case "Debug":
                actions.ShowDebugJob(job.Id);
                break;
            case "Restart":
                if (job.Status is JobStatus.Pending or JobStatus.Queued or JobStatus.Running or JobStatus.Blocked)
                    jobService.StopJob(job.Id);
                actions.ShowRerunJob(job.Id);
                break;
            case "Delete":
                actions.ShowDeleteJob(job.Id);
                break;
        }
    }

    /// <summary>
    /// Executes a Review/PR-column dropdown action: the same plan actions ReviewApp
    /// offers, opening the matching dialog (or transitioning directly for Set Completed).
    /// </summary>
    private static void HandleReviewAction(
        string tag,
        PlanFile plan,
        IPlanReaderService planService,
        BoardActions actions)
    {
        switch (tag)
        {
            case "CreatePr":
                actions.ShowCreatePr(plan.FolderName);
                break;
            case "ResetToDraft":
                actions.ShowResetToDraft(plan.FolderName);
                break;
            case "RequestChanges":
                actions.ShowSuggestChanges(plan.FolderName);
                break;
            case "SetCompleted":
                planService.TransitionState(plan.FolderName, PlanStatus.Completed);
                actions.Refresh();
                break;
            case "Discard":
                actions.ShowDiscardPlan(plan.FolderName);
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
        DateTime? TimerStartedAt,
        Action OnClick,
        string? PlanFolder,
        PlanFile? Plan,
        JobItem? MenuJob);

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
