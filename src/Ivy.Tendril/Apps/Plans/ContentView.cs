using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Plans.Dialogs;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Dialogs;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Apps.Views.Tabs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Plans;

public class ContentView(
    PlanFile? selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IPlanReaderService planService,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config,
    IGitService gitService,
    IChatExecutionService? chatExecutionService = null,
    IChatHistoryService? chatHistoryService = null) : ViewBase
{
    private const string PlanTab = "plan";
    private const string DetailsTab = "details";
    private const string GitTab = "git";

    private IChatExecutionService? _chatExecutionService = chatExecutionService;
    private IChatHistoryService? _chatHistoryService = chatHistoryService;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var nav = UseNavigation();
        var agentRunner = UseService<IAgentRunner>();
        var shareTunnelService = UseService<IShareTunnelService>();
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);
        var openFile = UseState<string?>(null);
        var selectedRepoState = UseState<string?>(null);
        var issueAssigneeState = UseState<string?>(null);
        var issueLabelsState = UseState<string[]>([]);
        var issueCommentState = UseState("");
        var showDirtyDialog = UseState(false);
        var draftAnnotationService = UseService<Ivy.Tendril.Services.Plans.IPlanAnnotationService>();
        var annotations = UseState(() => selectedPlan != null
            ? draftAnnotationService.GetAnnotationsForPlan(selectedPlan.FolderPath).ToImmutableList()
            : ImmutableList<MarkdownAnnotation>.Empty);
        var showAnnotationsDialog = UseState(false);
        var showQuestionsDialog = UseState(false);
        // The revision as the user is editing it. Answers are merged in here and written straight
        // back to the same revision file — answering a question is not a new revision of the plan,
        // it is filling in a blank the plan left.
        var revisionContent = UseState(() => selectedPlan?.LatestRevisionContent ?? "");
        var pendingWaitJobIds = UseState<List<string>?>((List<string>?)null);
        var shareContext = UseService<Ivy.Tendril.Services.Share.IShareContext>();
        var (runPreflight, isCheckingPreflight, preflightResult) = Context.UsePreflightCheck();

        var processView = Context.UseTendrilProcess();

        var (shareModal, showShareModal) = UseTrigger((isOpen) =>
            !isOpen.Value ? null : new ShareTunnelModal(isOpen, selectedPlan!.FolderName, isReview: false));

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
                new JobDebugSheet(jobId, jobService, planService, config, () => isOpen.Set(false)),
                "Job Debug"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var (costSheet, showCostJob) = UseTrigger<string>((isOpen, jobId) =>
        {
            if (!isOpen.Value) return null;
            return new Sheet(
                () => isOpen.Set(false),
                new JobCostSheetView(jobId, jobService),
                "Cost & Tokens"
            ).Width(UxHelper.SheetWidth).Resizable();
        });

        var isEditing = UseState(false);
        var editContent = UseState("");
        var originalContent = UseState("");
        var isEditingPrev = UseState(false);
        var lastPlanId = UseState(selectedPlan?.Id ?? -1);
        var lastContentHash = UseState(selectedPlan?.LatestRevisionContent?.GetHashCode() ?? 0);

        var selectedTab = UseState(PlanTab);
        // Brings a question into view when its dropdown entry is clicked. The token is what makes a
        // repeat click work — an unchanged id compares equal and nothing would move.
        var scrollTo = UseState<QuestionScrollTarget?>(() => null);
        var openVerification = UseState<string?>(null);
        var openCommit = UseState<string?>(null);

        var selectedPlanRef = UseRef(selectedPlan);

        var planContentQuery = UseQuery<PlanContentData, string>(
            selectedPlan?.FolderPath ?? "",
            async (folderPath, ct) => await Task.Run(() => LoadPlanContent(folderPath), ct),
            initialValue: new PlanContentData(null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(), null, new GitTabDataBuilder.GitTabData([], []))
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

        UseEffect(() =>
        {
            void OnAnnotationsChanged(string folderPath, List<MarkdownAnnotation> updated)
            {
                if (selectedPlanRef.Value != null && folderPath == selectedPlanRef.Value.FolderPath)
                {
                    annotations.Set(updated.ToImmutableList());
                }
            }

            draftAnnotationService.AnnotationsChanged += OnAnnotationsChanged;
            return Disposable.Create(() => draftAnnotationService.AnnotationsChanged -= OnAnnotationsChanged);
        });

#pragma warning disable CS8601
        selectedPlanRef.Value = selectedPlan;
#pragma warning restore CS8601

        // Keyed on the plan's identity, not on the state object: every refresh hands the state a
        // fresh PlanFile instance of the same plan, and that must not throw the reader back to the
        // first tab.
        if (lastPlanId.Value != (selectedPlan?.Id ?? -1))
        {
            lastPlanId.Set(selectedPlan?.Id ?? -1);
            selectedTab.Set(PlanTab);
            isEditing.Set(false);
            var loaded = selectedPlan != null
                ? draftAnnotationService.GetAnnotationsForPlan(selectedPlan.FolderPath).ToImmutableList()
                : ImmutableList<MarkdownAnnotation>.Empty;
            annotations.Set(loaded);
            showAnnotationsDialog.Set(false);
            // Left open across a switch, "Execute Anyway" would run the new plan on a confirmation
            // the user gave for the old one.
            showQuestionsDialog.Set(false);
            pendingWaitJobIds.Set((List<string>?)null);
        }

        // Annotation offsets anchor to the plan text; drop them if the content changed
        // underneath (plan updated, edited, or revised).
        var contentHash = selectedPlan?.LatestRevisionContent?.GetHashCode() ?? 0;
        if (lastContentHash.Value != contentHash)
        {
            lastContentHash.Set(contentHash);
            var loaded = selectedPlan != null
                ? draftAnnotationService.GetAnnotationsForPlan(selectedPlan.FolderPath).ToImmutableList()
                : ImmutableList<MarkdownAnnotation>.Empty;
            annotations.Set(loaded);
            revisionContent.Set(selectedPlan?.LatestRevisionContent ?? "");
        }

        if (selectedPlan is null)
            return BuildNoSelectionView(processView);

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var questions = QuestionAnswers.Read(revisionContent.Value);
        var answeredQuestions = questions.Count(q => q.HasAnswer);
        var unansweredQuestions = CountUnansweredQuestions(questions);

        void ApplyAnswer(QuestionAnswer answer)
        {
            if (!QuestionAnswers.TryApply(revisionContent.Value, answer, out var merged))
                return;

            revisionContent.Set(merged);

            // The plan snapshot has to carry the write as well. The guard above compares against
            // `selectedPlan`, which nothing else re-reads after a content write — leaving it stale
            // makes the guard fire on the very next render and revert the answer we just made.
            // Advancing both together keeps the guard quiet, which is what we want here: an answer
            // lands inside a fence, so the passages annotations point at have not moved.
            selectedPlanState.Set(selectedPlan with { LatestRevisionContent = merged });
            lastContentHash.Set(merged.GetHashCode());

            planService.UpdateLatestRevision(selectedPlan.FolderName, merged);
        }

        var isShareMode = shareContext.IsShareMode;
        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);
        var hasActiveExpandJob = HasActiveJob<ExpandPlanArgs>();
        var hasActiveSplitJob = HasActiveJob<SplitPlanArgs>();

        var actions = DraftActions.Build(new DraftActionsContext(
            selectedPlan, selectedPlanState, isEditing, editContent, originalContent,
            planService, jobService, config, client, nav, agentRunner, shareContext, shareTunnelService,
            _chatHistoryService, _chatExecutionService, isBeta,
            refreshPlans, copyToClipboard, showUpdateDialog, showDeleteDialog, showCreateIssueDialog, showShareModal,
            hasActiveExpandJob, hasActiveSplitJob));

        var activeAnnotationCount = annotations.Value.Count(a => !a.IsResolved);
        if (!isShareMode && !isEditing.Value)
        {
            // Both kinds of pending work go through one button, because one job answers both: an
            // UpdatePlan that folds them into the plan. The badge counts them together.
            if (activeAnnotationCount > 0 || answeredQuestions > 0)
            {
                actions.AddSecondary("UpdatePlan", "Update Plan", Icons.WandSparkles,
                    () => SubmitAnnotationsUpdate(annotations, answeredQuestions, draftAnnotationService),
                    disabled: HasActiveJob<UpdatePlanArgs>(),
                    badge: (activeAnnotationCount + answeredQuestions).ToString());
            }

            actions.SetPrimary("Execute", "Execute", Icons.Rocket, () => runPreflight(selectedPlan.Project, result =>
            {
                // Unincorporated work first, then unanswered questions: the former
                // would be ignored outright, the latter merely decided for you.
                if (activeAnnotationCount > 0 || answeredQuestions > 0)
                    showAnnotationsDialog.Set(true);
                else if (unansweredQuestions > 0)
                    showQuestionsDialog.Set(true);
                else
                    ContinueExecute(null, result, pendingWaitJobIds, showDirtyDialog);
            }), "x", disabled: isCheckingPreflight, loading: isCheckingPreflight);
        }

        var planTabContent = new PlanTabView(
            selectedPlan,
            selectedPlanState,
            isEditing.Value,
            editContent,
            openFile,
            planService,
            config,
            annotations,
            revisionContent,
            ApplyAnswer,
            scrollTo.Value,
            isShareMode ? shareContext.Persona : null);

        var tabs = new List<PlanTabDto> { new(PlanTab, "Plan"), new(DetailsTab, "Details") };
        object tabContent;

        if (planContentQuery.Loading)
        {
            tabContent = Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                         | Text.Muted("Loading...");
        }
        else
        {
            var planData = planContentQuery.Value;
            var gitData = planData.GitData ?? new GitTabDataBuilder.GitTabData([], []);
            var gitItemCount = GitTabDataBuilder.CountGitItems(gitData, selectedPlan);
            if (gitItemCount > 0)
                tabs.Add(new PlanTabDto(GitTab, "Git", gitItemCount.ToString()));

            var activeTab = tabs.Any(t => t.Id == selectedTab.Value) ? selectedTab.Value : PlanTab;
            tabContent = activeTab switch
            {
                DetailsTab => Cap(new DetailsTabView(selectedPlan,
                    jobService.GetJobsForPlan(selectedPlan.FolderName),
                    showDebugJob, showCostJob, planService, selectedPlanState, refreshPlans,
                    folderPath => selectedPlanState.Set(planService.GetPlanByFolder(folderPath)))),
                GitTab => Cap(new GitTabView(
                    gitData,
                    selectedPlan,
                    hash => openCommit.Set(hash),
                    path =>
                    {
                        copyToClipboard(path);
                        client.Toast("Copied path to clipboard", "Path Copied");
                        return null!;
                    },
                    null,
                    null)),
                // PlanMarkdown owns its own scroll, so the Plan tab is not wrapped in Cap().
                _ => planTabContent
            };
        }

        var effectiveTab = tabs.Any(t => t.Id == selectedTab.Value) ? selectedTab.Value : PlanTab;

        object? questionsPanel = questions.Count > 0
            ? new QuestionsPanelView(questions, id =>
            {
                selectedTab.Set(PlanTab);
                scrollTo.Set(new QuestionScrollTarget(id, (scrollTo.Value?.Token ?? 0) + 1));
            })
            : null;

        var workspace = actions.ApplyTo(new PlanWorkspace(
                tabContent,
                isShareMode ? null : new PlanChatView(selectedPlan),
                new VerificationsPanelView(selectedPlan, planService, config),
                questionsPanel)
            .PlanId($"#{selectedPlan.Id}")
            .Title(selectedPlan.Title)
            .Meta(BuildMeta(selectedPlan, currentIndex, allPlans.Count))
            .Source(
                string.IsNullOrEmpty(selectedPlan.SourceUrl) ? null : selectedPlan.SourceUrl,
                selectedPlan.IsPullRequestSource ? "PR" : "Issue")
            .Persona(
                isShareMode ? shareContext.Persona : null,
                isShareMode ? Ivy.Tendril.Services.Share.AnonymousPersonaGenerator.GetInitials(shareContext.Persona) : null)
            .Tabs(tabs)
            .SelectedTab(effectiveTab)
            .QuestionsLabel(unansweredQuestions > 0 ? $"Questions ({unansweredQuestions} unanswered)" : "Questions")
            .UnansweredQuestions(unansweredQuestions)
            .OnTabSelect(id => selectedTab.Set(id)))
            .WithLayout().Full().RemoveParentPadding()
            .Key(selectedPlan.Id);

        var dirtyRepoDialog = showDirtyDialog.Value && preflightResult is { DirtyRepos.Count: > 0 }
            ? new DirtyRepoDialog(
                showDirtyDialog,
                preflightResult,
                proceedLabel: "Create Without Syncing",
                contextMessage: "These changes will NOT be included in this plan. The plan will execute against origin/<baseBranch>. If these changes are meant for this plan, commit and push them first.",
                onSyncRepos: policy =>
                {
                    LaunchWithSync(preflightResult, pendingWaitJobIds.Value, policy);
                    pendingWaitJobIds.Set((List<string>?)null);
                },
                onProceed: () =>
                {
                    LaunchExecute(pendingWaitJobIds.Value);
                    pendingWaitJobIds.Set((List<string>?)null);
                })
            : null;

        var annotationsDialog = BuildAnnotationsGuardDialog(
            annotations, answeredQuestions, unansweredQuestions, showAnnotationsDialog,
            showQuestionsDialog, preflightResult, pendingWaitJobIds, showDirtyDialog, draftAnnotationService);

        var questionsDialog = showQuestionsDialog.Value && unansweredQuestions > 0
            ? new UnansweredQuestionsDialog(
                showQuestionsDialog,
                unansweredQuestions,
                onContinue: () => ContinueExecute(null, preflightResult, pendingWaitJobIds, showDirtyDialog))
            : null;

        var elements = new List<object>
        {
            workspace,
            updateDialog,
            deleteDialog,
            createIssueDialog,
            debugSheet,
            costSheet,
            new VerificationReportSheet(openVerification, selectedPlan, config),
            new CommitDetailSheet(openCommit, selectedPlan, config, gitService)
        };

        if (isBeta || isShareMode)
            elements.Add(shareModal);

        if (dirtyRepoDialog is not null)
            elements.Add(dirtyRepoDialog);

        if (annotationsDialog is not null)
            elements.Add(annotationsDialog);

        if (questionsDialog is not null)
            elements.Add(questionsDialog);

        elements.Add(new FileSheet(openFile, config));

        return new Fragment(elements.ToArray());

        // The workspace inset: 24px top, 32px sides, matching what PlanMarkdown applies to itself.
        object Cap(object inner) => Layout.Vertical().Scroll().HideScrollbar().Width(Size.Full()).Height(Size.Full())
            | (Layout.Vertical()
                .Padding(8, 6, 8, 4)
                .Width(Size.Full().Max(Size.Units(200))) | inner);
    }

    internal static string BuildMeta(PlanFile plan, int currentIndex, int total)
    {
        var meta = $"{currentIndex + 1}/{total} plans";
        if (plan.DependsOn.Count == 0)
            return meta;

        var depIds = string.Join(", ", plan.DependsOn.Select(d =>
        {
            var name = Path.GetFileName(d);
            var dashIdx = name.IndexOf('-');
            var idStr = dashIdx > 0 ? name[..dashIdx] : name;
            return int.TryParse(idStr, out var id) ? $"#{id}" : idStr;
        }));
        return $"{meta} · Depends on {depIds}";
    }

    private object BuildNoSelectionView(object processView)
    {
        if (allPlans.Count == 0)
            return new NoContentView("No plans", "Plans you create will appear here", processView);

        return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
               | Text.Muted("Select a plan from the sidebar");
    }

    private PlanContentData LoadPlanContent(string folderPath)
    {
        if (selectedPlan is null)
            return new PlanContentData(null,
                new Dictionary<string, List<string>>(), [],
                new Dictionary<string, bool>(), null,
                new GitTabDataBuilder.GitTabData([], []));

        var summaryPath = Path.Combine(folderPath, "Artifacts", "summary.md");
        var summaryMd = File.Exists(summaryPath) ? FileHelper.ReadAllText(summaryPath) : null;

        var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

        var commitRows = PlanContentHelpers.BuildCommitRows(selectedPlan, config, gitService);

        var gitData = GitTabDataBuilder.BuildGitTabData(commitRows, selectedPlan, config, gitService);

        var allChanges = PlanContentHelpers.GetAllChangesData(selectedPlan, config, gitService);

        var verReports = selectedPlan.Verifications.ToDictionary(
            v => v.Name,
            v => File.Exists(Path.Combine(folderPath, "Verification", $"{v.Name}.md")));

        return new PlanContentData(summaryMd, artifacts, commitRows, verReports, allChanges, gitData);
    }

    private PendingAnnotationsDialog? BuildAnnotationsGuardDialog(
        IState<ImmutableList<MarkdownAnnotation>> annotations,
        int answeredQuestions,
        int unansweredQuestions,
        IState<bool> showAnnotationsDialog,
        IState<bool> showQuestionsDialog,
        PreflightResult? preflightResult,
        IState<List<string>?> pendingWaitJobIds,
        IState<bool> showDirtyDialog,
        Ivy.Tendril.Services.Plans.IPlanAnnotationService draftAnnotationService)
    {
        var activeCount = annotations.Value.Count(a => !a.IsResolved);
        if (!showAnnotationsDialog.Value || (activeCount == 0 && answeredQuestions == 0))
            return null;

        return new PendingAnnotationsDialog(
            showAnnotationsDialog,
            activeCount,
            answeredQuestions,
            onUpdate: () => SubmitAnnotationsUpdate(annotations, answeredQuestions, draftAnnotationService),
            // Updating retires the questions it folds in, so there is nothing left to warn about
            // on this path — the warning would be about a state the job is on its way to fixing.
            onUpdateAndExecute: () => ContinueExecute(
                [SubmitAnnotationsUpdate(annotations, answeredQuestions, draftAnnotationService)], preflightResult, pendingWaitJobIds,
                showDirtyDialog),
            onDiscardAndExecute: () =>
            {
                // Only annotations are discarded. Answers live in the revision file, so executing
                // without updating leaves them there for the agent to honour as written.
                annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
                if (selectedPlan != null)
                    _ = draftAnnotationService.ClearAnnotationsAsync(selectedPlan.FolderPath);

                // Declining the update does not settle the open questions, so that warning is still
                // owed — otherwise having annotations would quietly suppress it.
                if (unansweredQuestions > 0)
                    showQuestionsDialog.Set(true);
                else
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

    internal static object BuildFailureCallout(PlanFile plan, string tendrilHome)
    {
        return BuildVerificationFailureCallout(plan) ?? BuildLogFailureCallout(plan, tendrilHome);
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

    private static object BuildLogFailureCallout(PlanFile plan, string tendrilHome)
    {
        var planId = JobLogPaths.PlanIdFromFolderName(Path.GetFileName(plan.FolderPath));
        var lastLog = planId == null
            ? null
            : JobLogPaths.LogsForPlanId(tendrilHome, planId).LastOrDefault();
        if (lastLog == null)
            return Callout.Destructive("No details available. Check the job logs.", "Execution Failed");

        var logContent = FileHelper.ReadAllText(lastLog);
        // "Final Output" is the heading JobLogWriter actually emits — the agent's last response, which is
        // the most useful thing to surface on a failed plan.
        var summary = MatchSection(logContent, "Final Output");
        if (summary != null)
            return Callout.Destructive(summary, "Execution Failed");

        var statusMatch = Regex.Match(logContent, @"\*\*Status:\*\*\s*(.+)");
        if (!statusMatch.Success)
            return Callout.Destructive("No details available. Check the job logs.", "Execution Failed");
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

    internal void LaunchExecute(List<string>? waitJobIds = null)
    {
        if (selectedPlan is null) return;

        var hasWaits = waitJobIds is { Count: > 0 };

        // When chained behind an UpdatePlan job the plan is already Updating;
        // JobLauncher sets Executing once the blocked ExecutePlan launches.
        if (!hasWaits)
            TransitionPlanOptimistically(PlanStatus.Creating);

        var jobId = jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath)
        {
            WaitForJobs = hasWaits ? waitJobIds : null,
            ChatSessionId = selectedPlan.ChatSessionId
        });
        EmitManualExecutionEvent(jobId);
        refreshPlans();
    }

    private void LaunchWithSync(PreflightResult preflight, List<string>? waitJobIds = null,
        UntrackedChangesPolicy policy = UntrackedChangesPolicy.Stash)
    {
        if (selectedPlan is null) return;

        var hasWaits = waitJobIds is { Count: > 0 };
        var allWaitIds = hasWaits ? new List<string>(waitJobIds!) : new List<string>();
        foreach (var (repoPath, baseBranch, _) in preflight.DirtyRepos)
        {
            var jobId = jobService.StartJob(new SyncRepoArgs(repoPath, baseBranch, selectedPlan.FolderPath, policy));
            allWaitIds.Add(jobId);
        }

        // When chained behind an UpdatePlan job the plan is already Updating;
        // JobLauncher sets Executing once the blocked ExecutePlan launches.
        if (!hasWaits)
            TransitionPlanOptimistically(PlanStatus.Creating);

        var executeJobId = jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath)
        {
            WaitForJobs = allWaitIds,
            ChatSessionId = selectedPlan.ChatSessionId
        });
        EmitManualExecutionEvent(executeJobId);
        refreshPlans();
    }

    internal void EmitManualExecutionEvent(string jobId)
    {
        if (selectedPlan is null) return;
        var chatSessionId = selectedPlan.ChatSessionId;
        if (string.IsNullOrEmpty(chatSessionId))
        {
            chatSessionId = jobService.GetJob(jobId)?.ChatSessionId;
        }
        if (string.IsNullOrEmpty(chatSessionId)) return;

        var chatService = _chatHistoryService;

        if (chatService != null)
        {
            try
            {
                var session = chatService.GetSession(chatSessionId);
                if (session?.Messages != null)
                {
                    foreach (var msg in session.Messages)
                    {
                        if (string.IsNullOrWhiteSpace(msg.Content)) continue;

                        var summaries = QuestionAnswers.Read(msg.Content);
                        var unanswered = summaries.Where(q => !q.HasAnswer).ToList();
                        if (unanswered.Count == 0) continue;

                        var parsedBlocks = QuestionBlockParser.Parse(msg.Content);
                        var answersToApply = new Dictionary<string, string[]>();

                        foreach (var qSummary in unanswered)
                        {
                            var isApprovalQuestion =
                                qSummary.Id.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                                qSummary.Id.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
                                qSummary.Id.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                                qSummary.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                                qSummary.Title.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
                                qSummary.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase);

                            if (!isApprovalQuestion) continue;

                            PlanQuestion? matchedQuestion = null;
                            foreach (var pb in parsedBlocks)
                            {
                                if (pb.Block?.Questions != null)
                                {
                                    matchedQuestion = pb.Block.Questions.FirstOrDefault(q => q.Id == qSummary.Id);
                                    if (matchedQuestion != null) break;
                                }
                            }

                            if (matchedQuestion?.Options is { Count: > 0 } options)
                            {
                                var recommendedOption = options.FirstOrDefault(o => o.Recommended);
                                var approvalOption = recommendedOption ?? options.FirstOrDefault(o =>
                                    o.Value.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                                    o.Value.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
                                    o.Value.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                                    o.Value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
                                    o.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                                    o.Title.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
                                    o.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                                    o.Title.Contains("yes", StringComparison.OrdinalIgnoreCase)) ?? options[0];

                                answersToApply[qSummary.Id] = [approvalOption.Value];
                            }
                        }

                        if (answersToApply.Count > 0)
                        {
                            chatService.ApplyQuestionAnswers(chatSessionId, msg.Id, answersToApply);
                        }
                    }
                }
            }
            catch
            {
                // Gracefully handle question resolution errors
            }
        }

        var chatExec = _chatExecutionService;
        if (chatExec is null) return;

        var message = $"[System Event] Manual approval granted and execution started for plan '{selectedPlan.Title}' (Job {jobId}).";
        _ = Task.Run(async () =>
        {
            try
            {
                await chatExec.SendMessageAsync(chatSessionId, message, role: "system");
            }
            catch
            {
                // Gracefully handle dispatch errors
            }
        });
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

    private string SubmitAnnotationsUpdate(
        IState<ImmutableList<MarkdownAnnotation>> annotations,
        int answeredQuestions,
        Ivy.Tendril.Services.Plans.IPlanAnnotationService draftAnnotationService)
    {
        var prompt = BuildUpdatePrompt(annotations.Value.Where(a => !a.IsResolved), answeredQuestions);

        TransitionPlanOptimistically(PlanStatus.Updating);
        var jobId = jobService.StartJob(new UpdatePlanArgs(selectedPlan!.FolderPath, prompt));
        annotations.Set(ImmutableList<MarkdownAnnotation>.Empty);
        if (selectedPlan != null)
            _ = draftAnnotationService.ClearAnnotationsAsync(selectedPlan.FolderPath);
        refreshPlans();
        return jobId;
    }

    /// <summary>
    ///     Instructions for the update job. Annotations have to be quoted here because they live
    ///     only in the UI; answers do not, because they are already written into the revision the
    ///     agent is about to read. It only needs telling that they are there, and what to do with
    ///     the questions that were left alone.
    /// </summary>
    internal static string BuildUpdatePrompt(
        IEnumerable<MarkdownAnnotation> annotations,
        int answeredQuestions = 0)
    {
        var annotationList = annotations.ToList();
        var sb = new StringBuilder();

        if (answeredQuestions > 0)
        {
            var noun = answeredQuestions == 1 ? "question" : "questions";
            sb.AppendLine($"I answered {answeredQuestions} {noun} in this plan's `questions` blocks.");
            sb.AppendLine("The answers are already in the revision as `answer` keys. Treat each one as my");
            sb.AppendLine("decision: fold it into the plan as concrete prose or steps, then delete that");
            sb.AppendLine("question from its block, dropping the block once its last question goes.");
            sb.AppendLine("Carry any question I left unanswered forward unchanged — do not answer it for");
            sb.AppendLine("me, and do not reword it.");

            if (annotationList.Count > 0)
                sb.AppendLine();
        }

        if (annotationList.Count == 0)
            return sb.ToString().TrimEnd();

        sb.AppendLine("I reviewed the plan and left inline annotations on specific passages.");
        sb.AppendLine("Revise the plan to address every annotation below. Each item quotes the");
        sb.AppendLine("passage I selected, followed by my comment about it.");

        var index = 1;
        foreach (var annotation in annotationList)
        {
            sb.AppendLine();
            sb.AppendLine(!string.IsNullOrEmpty(annotation.Author)
                ? $"## Annotation {index} (by {annotation.Author})"
                : $"## Annotation {index}");
            sb.AppendLine("Selected text:");
            foreach (var line in annotation.SelectedText.Split('\n'))
                sb.AppendLine($"> {line.TrimEnd('\r')}");
            sb.AppendLine();
            sb.AppendLine($"Comment: {annotation.Comment}");
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    internal static int CountUnansweredQuestions(IEnumerable<QuestionSummary> questions) =>
        questions.Count(q => !q.HasAnswer);

    internal static int CountUnansweredQuestions(string? revisionContent) =>
        string.IsNullOrEmpty(revisionContent)
            ? 0
            : CountUnansweredQuestions(QuestionAnswers.Read(revisionContent));

    private record PlanContentData(
        string? SummaryMarkdown,
        Dictionary<string, List<string>> Artifacts,
        List<PlanContentHelpers.CommitRow> CommitRows,
        Dictionary<string, bool> VerificationReports,
        PlanContentHelpers.AllChangesData? AllChanges,
        GitTabDataBuilder.GitTabData? GitData);
}
