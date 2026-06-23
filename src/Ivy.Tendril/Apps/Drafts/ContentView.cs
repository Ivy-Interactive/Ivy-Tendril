using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Ivy.Core;
using Ivy.Tendril.Apps.Drafts.Dialogs;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Dialogs;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Apps.Views.Tabs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Drafts;

public class ContentView(
    PlanFile? selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IPlanReaderService planService,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config,
    IGitService gitService) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var openFile = UseState<string?>(null);
        var selectedRepoState = UseState<string?>(null);
        var issueAssigneeState = UseState<string?>(null);
        var issueLabelsState = UseState<string[]>([]);
        var issueCommentState = UseState("");
        var showDirtyDialog = UseState(false);
        var annotations = UseState(ImmutableList<MarkdownAnnotation>.Empty);
        var showAnnotationsDialog = UseState(false);
        var pendingWaitJobIds = UseState<List<string>?>((List<string>?)null);
        var (runPreflight, isCheckingPreflight, preflightResult) = Context.UsePreflightCheck();

        var processView = Context.UseTendrilProcess();

        var (updateDialog, showUpdateDialog) = UseTrigger((isOpen) => !isOpen.Value ? null : new UpdatePlanDialog(isOpen, selectedPlan!, selectedPlanState, jobService, refreshPlans));

        var (deleteDialog, showDeleteDialog) = UseTrigger((isOpen) => !isOpen.Value ? null : new DeletePlanDialog(isOpen, selectedPlan!, selectedPlanState, planService, refreshPlans));

        var (createIssueDialog, showCreateIssueDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new CreateIssueDialog(isOpen, selectedRepoState, issueAssigneeState, issueLabelsState,
                issueCommentState, selectedPlan!, jobService);
        });

        var (debugSheet, showDebugJob) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            return new Sheet(
                () => isOpen.Set(false),
                new JobDebugSheet(jobId, jobService, planService, config),
                "Job Debug"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var isEditing = UseState(false);
        var editContent = UseState("");
        var originalContent = UseState("");
        var isEditingPrev = UseState(false);
        var lastPlanId = UseState(selectedPlan?.Id ?? -1);
        var lastContentHash = UseState(selectedPlan?.LatestRevisionContent?.GetHashCode() ?? 0);

        var selectedTab = UseState(0);
        var openVerification = UseState<string?>(null);
        var openCommit = UseState<string?>(null);

        var selectedPlanRef = UseRef(selectedPlan);

        var planContentQuery = UseQuery<PlanContentData, string>(
            selectedPlan?.FolderPath ?? "",
            async (folderPath, ct) => await Task.Run(() => LoadPlanContent(folderPath), ct),
            initialValue: new PlanContentData(null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(), null)
        );

        // Authentication effects (was UseAuthenticationEffects)
        UseEffect(() =>
        {
            var plan = selectedPlanRef.Value;
            if (isEditing.Value && !isEditingPrev.Value)
            {
                if (plan != null)
                {
                    var raw = planService.ReadRawPlan(plan.FolderName);
                    editContent.Set(raw);
                    originalContent.Set(raw);
                }
                else
                {
                    isEditing.Set(false);
                }
            }

            isEditingPrev.Set(isEditing.Value);
        }, isEditing);

        // Navigation effects (was UseNavigationEffects)
        UseEffect(() => { selectedTab.Set(0); }, selectedPlanState);

#pragma warning disable CS8601
        selectedPlanRef.Value = selectedPlan;
#pragma warning restore CS8601

        if (lastPlanId.Value != (selectedPlan?.Id ?? -1))
        {
            lastPlanId.Set(selectedPlan?.Id ?? -1);
            isEditing.Set(false);
            annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
            showAnnotationsDialog.Set(false);
            pendingWaitJobIds.Set((List<string>?)null);
        }

        // Annotation offsets anchor to the plan text; drop them if the content changed
        // underneath (plan updated, edited, or revised).
        var contentHash = selectedPlan?.LatestRevisionContent?.GetHashCode() ?? 0;
        if (lastContentHash.Value != contentHash)
        {
            lastContentHash.Set(contentHash);
            annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
        }

        if (selectedPlan is null)
            return BuildNoSelectionView(processView);

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var desktopTitleLayout = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
            | new Box(Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis))
                .BorderThickness(0).Padding(0).Width(Size.Fit().Min(Size.Px(0)));

        if (!string.IsNullOrEmpty(selectedPlan.SourceUrl))
            desktopTitleLayout |= new Button(selectedPlan.SourceUrl.Contains("/pull/") ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => client.OpenUrl(selectedPlan.SourceUrl));

        if (selectedPlan.DependsOn.Count > 0)
        {
            var depIds = string.Join(", ", selectedPlan.DependsOn.Select(d =>
            {
                var name = Path.GetFileName(d);
                var dashIdx = name.IndexOf('-');
                var idStr = dashIdx > 0 ? name[..dashIdx] : name;
                return int.TryParse(idStr, out var id) ? $"#{id}" : idStr;
            }));
            desktopTitleLayout |= new Badge($"Depends on: {depIds}").Variant(BadgeVariant.Secondary);
        }

        var desktopTitle = new Box(desktopTitleLayout).BorderThickness(0).Padding(0)
            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var titleArea = Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow())
                        | desktopTitle
                        | MobileItemPicker.Build(
                                $"#{selectedPlan.Id} {selectedPlan.Title}",
                                allPlans,
                                p => $"#{p.Id} {p.Title}",
                                p => p.FolderName == selectedPlan.FolderName,
                                p => selectedPlanState.Set(p))
                            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var controls = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                       | Text.Rich()
                           .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                           .Muted("plans", word: true);

        if (annotations.Value.Count > 0)
            controls |= BuildAnnotationsUpdateButton(annotations);

        controls |= new Button("Execute").Icon(Icons.Rocket).Primary().ShortcutKey("x")
                        .Loading(isCheckingPreflight)
                        .Disabled(isCheckingPreflight)
                        .OnClick(() => runPreflight(selectedPlan.Project, result =>
                        {
                            if (annotations.Value.Count > 0)
                                showAnnotationsDialog.Set(true);
                            else
                                ContinueExecute(null, result, pendingWaitJobIds, showDirtyDialog);
                        }));

        var header = Layout.Horizontal().Height(Size.Px(40)).Width(Size.Full()).Gap(2).AlignContent(Align.Left)
                     | titleArea
                     | controls;

        var content = Layout.Vertical().Height(Size.Full());

        var planTabContent = new PlanTabView(
            selectedPlan,
            selectedPlanState,
            isEditing.Value,
            editContent,
            openFile,
            planService,
            config,
            annotations);

        if (planContentQuery.Loading)
        {
            content |= Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                       | Text.Muted("Loading...");
        }
        else
        {
            var tabs = Layout.Tabs(
                // DraftMarkdown owns its own scroll and the pinned StickyContent slot,
                // so it is not wrapped in Cap() (whose outer scroll would also scroll the
                // pinned element). The widget reproduces Cap()'s left inset + max-width.
                new Tab("Plan", planTabContent),
                new Tab("Details", Cap(new DetailsTabView(selectedPlan!,
                    jobService.GetJobsForPlan(selectedPlan!.FolderName),
                    showDebugJob, planService, selectedPlanState, refreshPlans,
                    folderPath => selectedPlanState.Set(planService.GetPlanByFolder(folderPath)))))
            ).OnSelect(v => selectedTab.Set(v)).SelectedIndex(selectedTab.Value).Variant(TabsVariant.Content).RemoveParentPadding();

            content |= (Layout.Vertical().Padding(2).Height(Size.Full()) | tabs);
        }

        content |= new VerificationReportSheet(openVerification, selectedPlan);
        content |= new CommitDetailSheet(openCommit, selectedPlan, config, gitService);

        var hasActiveExpandJob = HasActiveJob<ExpandPlanArgs>();
        var hasActiveSplitJob = HasActiveJob<SplitPlanArgs>();

        var actionBar = new ActionBarView(
            selectedPlan,
            allPlans,
            selectedPlanState,
            isEditing,
            editContent,
            originalContent,
            showUpdateDialog,
            showDeleteDialog,
            showCreateIssueDialog,
            planService,
            jobService,
            config,
            refreshPlans,
            copyToClipboard,
            hasActiveExpandJob,
            hasActiveSplitJob,
            GoToNext,
            GoToPrevious);

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Scroll(Scroll.None).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlan.Id);

        var dirtyRepoDialog = showDirtyDialog.Value && preflightResult is { DirtyRepos.Count: > 0 }
            ? new DirtyRepoDialog(
                showDirtyDialog,
                preflightResult,
                proceedLabel: "Execute Anyway",
                contextMessage: "These changes will NOT be included in this plan. The plan will execute against origin/<baseBranch>. If these changes are meant for this plan, commit and push them first.",
                onSyncRepos: () =>
                {
                    LaunchWithSync(preflightResult, pendingWaitJobIds.Value);
                    pendingWaitJobIds.Set((List<string>?)null);
                },
                onProceed: () =>
                {
                    LaunchExecute(pendingWaitJobIds.Value);
                    pendingWaitJobIds.Set((List<string>?)null);
                })
            : null;

        var annotationsDialog = BuildAnnotationsGuardDialog(
            annotations, showAnnotationsDialog, preflightResult, pendingWaitJobIds, showDirtyDialog);

        var elements = new List<object>
        {
            mainLayout,
            updateDialog,
            deleteDialog,
            createIssueDialog,
            debugSheet
        };

        if (dirtyRepoDialog is not null)
            elements.Add(dirtyRepoDialog);

        if (annotationsDialog is not null)
            elements.Add(annotationsDialog);

        elements.Add(new FileSheet(openFile, config));

        return new Fragment(elements.ToArray());

        object Cap(object inner) => Layout.Vertical().Scroll().HideScrollbar().Width(Size.Full()).Height(Size.Full())
            | (Layout.Vertical()
                .Padding(6, 0, 0, 4)
                .Width(Size.Full().Max(Size.Units(200))) | inner);
    }

    private object BuildNoSelectionView(object processView)
    {
        if (allPlans.Count == 0)
            return new NoContentView("No draft plans", "Plans you create will appear here", processView);

        return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
               | Text.Muted("Select a plan from the sidebar");
    }

    private PlanContentData LoadPlanContent(string folderPath)
    {
        if (selectedPlan is null)
            return new PlanContentData(null,
                new Dictionary<string, List<string>>(), [],
                new Dictionary<string, bool>(), null);

        var summaryPath = Path.Combine(folderPath, "Artifacts", "summary.md");
        var summaryMd = File.Exists(summaryPath) ? FileHelper.ReadAllText(summaryPath) : null;

        var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

        var commitRows = PlanContentHelpers.BuildCommitRows(selectedPlan, config, gitService);

        var allChanges = PlanContentHelpers.GetAllChangesData(selectedPlan, config, gitService);

        var verReports = selectedPlan.Verifications.ToDictionary(
            v => v.Name,
            v => File.Exists(Path.Combine(folderPath, "Verification", $"{v.Name}.md")));

        return new PlanContentData(summaryMd, artifacts, commitRows, verReports, allChanges);
    }

    private PendingAnnotationsDialog? BuildAnnotationsGuardDialog(
        IState<ImmutableList<MarkdownAnnotation>> annotations,
        IState<bool> showAnnotationsDialog,
        PreflightResult? preflightResult,
        IState<List<string>?> pendingWaitJobIds,
        IState<bool> showDirtyDialog)
    {
        if (!showAnnotationsDialog.Value || annotations.Value.Count == 0) return null;

        return new PendingAnnotationsDialog(
            showAnnotationsDialog,
            annotations.Value.Count,
            onUpdate: () => SubmitAnnotationsUpdate(annotations),
            onUpdateAndExecute: () => ContinueExecute(
                [SubmitAnnotationsUpdate(annotations)], preflightResult, pendingWaitJobIds, showDirtyDialog),
            onDiscardAndExecute: () =>
            {
                annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
                ContinueExecute(null, preflightResult, pendingWaitJobIds, showDirtyDialog);
            });
    }

    private void ContinueExecute(
        List<string>? waitJobIds,
        PreflightResult? result,
        IState<List<string>?> pendingWaitJobIds,
        IState<bool> showDirtyDialog)
    {
        if (result is { DirtyRepos.Count: > 0 })
        {
            pendingWaitJobIds.Set(waitJobIds);
            showDirtyDialog.Set(true);
        }
        else
        {
            LaunchExecute(waitJobIds);
        }
    }

    internal static object BuildFailureCallout(PlanFile plan)
    {
        return BuildVerificationFailureCallout(plan) ?? BuildLogFailureCallout(plan);
    }

    private static object? BuildVerificationFailureCallout(PlanFile plan)
    {
        var verificationDir = Path.Combine(plan.FolderPath, "Verification");
        var failedVerifications = plan.Verifications
            .Where(v => v.Status is VerificationStatus.Fail or VerificationStatus.Pending)
            .ToList();

        if (failedVerifications.Count == 0 || !Directory.Exists(verificationDir))
            return null;

        var parts = new List<string>();
        foreach (var v in failedVerifications)
        {
            var reportPath = Path.Combine(verificationDir, $"{v.Name}.md");
            if (!File.Exists(reportPath))
            {
                parts.Add($"**{v.Name}** {v.Status}, no report generated");
                continue;
            }

            var report = FileHelper.ReadAllText(reportPath);
            var detail = MatchSection(report, "Output")
                         ?? MatchSection(report, "Issues Found")
                         ?? "See verification report for details";
            parts.Add($"**{v.Name}** {detail}");
        }

        return Callout.Destructive(string.Join("\n\n", parts), "Execution Failed");
    }

    private static object BuildLogFailureCallout(PlanFile plan)
    {
        var logsDir = Path.Combine(plan.FolderPath, "Logs");
        if (!Directory.Exists(logsDir))
            return Callout.Destructive("No details available. Check the logs folder.", "Execution Failed");
        var lastLog = Directory.GetFiles(logsDir, "*.md")
            .OrderByDescending(f => f)
            .FirstOrDefault();
        if (lastLog == null)
            return Callout.Destructive("No details available. Check the logs folder.", "Execution Failed");

        var logContent = FileHelper.ReadAllText(lastLog);
        var summary = MatchSection(logContent, "Summary");
        if (summary != null)
            return Callout.Destructive(summary, "Execution Failed");

        var statusMatch = Regex.Match(logContent, @"\*\*Status:\*\*\s*(.+)");
        if (!statusMatch.Success)
            return Callout.Destructive("No details available. Check the logs folder.", "Execution Failed");
        var status = statusMatch.Groups[1].Value.Trim();
        if (status == nameof(PlanStatus.Completed))
            return Callout.Warning(
                "Execution reported as completed but plan is in Failed state. The process may have crashed during state transition.",
                "State Mismatch");
        return Callout.Destructive($"Last execution status: {status}", "Execution Failed");
    }

    private static string? MatchSection(string content, string sectionName)
    {
        var match = Regex.Match(content, $@"## {Regex.Escape(sectionName)}\s*\n([\s\S]*?)(?=\n## |\z)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private void LaunchExecute(List<string>? waitJobIds = null)
    {
        if (selectedPlan is null) return;

        var hasWaits = waitJobIds is { Count: > 0 };

        // When chained behind an UpdatePlan job the plan is already Updating;
        // JobLauncher sets Executing once the blocked ExecutePlan launches.
        if (!hasWaits)
            TransitionPlanOptimistically(PlanStatus.Creating);

        jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath) { WaitForJobs = hasWaits ? waitJobIds : null });
        refreshPlans();
    }

    private void LaunchWithSync(PreflightResult preflight, List<string>? waitJobIds = null)
    {
        if (selectedPlan is null) return;

        var hasWaits = waitJobIds is { Count: > 0 };
        var allWaitIds = hasWaits ? new List<string>(waitJobIds!) : new List<string>();
        foreach (var (repoPath, baseBranch, _) in preflight.DirtyRepos)
        {
            var jobId = jobService.StartJob(new SyncRepoArgs(repoPath, baseBranch, selectedPlan.FolderPath));
            allWaitIds.Add(jobId);
        }

        // When chained behind an UpdatePlan job the plan is already Updating;
        // JobLauncher sets Executing once the blocked ExecutePlan launches.
        if (!hasWaits)
            TransitionPlanOptimistically(PlanStatus.Creating);

        jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath) { WaitForJobs = allWaitIds });
        refreshPlans();
    }

    // Optimistically update UI state; the authoritative plan transition (and pre-state
    // snapshot) is performed by JobService.StartJob.
    private void TransitionPlanOptimistically(PlanStatus status)
    {
        var optimisticPlan = selectedPlan! with
        {
            Metadata = selectedPlan.Metadata with { State = status }
        };
        selectedPlanState.Set(optimisticPlan);
    }

    private bool HasActiveJob<TArgs>() where TArgs : JobArgsBase
    {
        return jobService.GetJobs().Any(j =>
            j is { TypedArgs: TArgs, Status: JobStatus.Running or JobStatus.Queued or JobStatus.Pending } &&
            j.TypedArgs.PlanFolder != null &&
            j.TypedArgs.PlanFolder.Equals(selectedPlan!.FolderPath, StringComparison.OrdinalIgnoreCase));
    }

    private Button BuildAnnotationsUpdateButton(IState<ImmutableList<MarkdownAnnotation>> annotations)
    {
        return new Button("Update Plan")
            .Icon(Icons.WandSparkles)
            .Primary()
            .Badge(annotations.Value.Count.ToString())
            .Disabled(HasActiveJob<UpdatePlanArgs>())
            .Tooltip("Update the plan from your annotations")
            .OnClick(() => SubmitAnnotationsUpdate(annotations));
    }

    private string SubmitAnnotationsUpdate(IState<ImmutableList<MarkdownAnnotation>> annotations)
    {
        var prompt = BuildAnnotationsPrompt(annotations.Value);

        TransitionPlanOptimistically(PlanStatus.Updating);
        var jobId = jobService.StartJob(new UpdatePlanArgs(selectedPlan!.FolderPath, prompt));
        annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
        refreshPlans();
        return jobId;
    }

    internal static string BuildAnnotationsPrompt(IEnumerable<MarkdownAnnotation> annotations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("I reviewed the plan and left inline annotations on specific passages.");
        sb.AppendLine("Revise the plan to address every annotation below. Each item quotes the");
        sb.AppendLine("passage I selected, followed by my comment about it.");

        var index = 1;
        foreach (var annotation in annotations)
        {
            sb.AppendLine();
            sb.AppendLine($"## Annotation {index}");
            sb.AppendLine("Selected text:");
            foreach (var line in annotation.SelectedText.Split('\n'))
                sb.AppendLine($"> {line.TrimEnd('\r')}");
            sb.AppendLine();
            sb.AppendLine($"Comment: {annotation.Comment}");
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    private void GoToNext()
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan?.FolderName);
        var nextIndex = (currentIndex + 1) % allPlans.Count;
        selectedPlanState.Set(allPlans[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan?.FolderName);
        var prevIndex = (currentIndex - 1 + allPlans.Count) % allPlans.Count;
        selectedPlanState.Set(allPlans[prevIndex]);
    }

    private record PlanContentData(
        string? SummaryMarkdown,
        Dictionary<string, List<string>> Artifacts,
        List<PlanContentHelpers.CommitRow> CommitRows,
        Dictionary<string, bool> VerificationReports,
        PlanContentHelpers.AllChangesData? AllChanges);
}
