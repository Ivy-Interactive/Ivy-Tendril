using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Apps.Review.Tabs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Apps.Views.Tabs;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Tendril.Services.Share;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Apps.Views.Dialogs;
using Microsoft.Extensions.Logging;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Review;

public class ContentView(
    IState<PlanFile?> selectedPlanState,
    List<PlanFile> allPlans,
    IPlanReaderService planService,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config,
    IGitService gitService) : ViewBase
{
    private const string SummaryTab = "summary";
    private const string PlanTab = "plan";
    private const string DetailsTab = "details";
    private const string GitTab = "git";
    private const string ChangesTab = "changes";
    private const string ArtifactsTab = "artifacts";
    private const string RecommendationsTab = "recommendations";

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<ContentView>>();
        var copyToClipboard = UseClipboard();
        var openVerification = UseState<string?>(null);
        var openArtifact = UseState<string?>(null);
        var openFile = UseState<string?>(null);
        var openCommit = UseState<string?>(null);
        var syncingWorktrees = UseState(new HashSet<string>());
        var selectedRecTitles = UseState(() => new HashSet<string>());
        var selectedTab = UseState(SummaryTab);
        var lastPlanFolder = UseState<string?>(() => selectedPlanState.Value?.FolderName);
        var draftDiffCommentService = UseService<Ivy.Tendril.Services.Plans.IPlanDiffCommentService>();
        var draftComments = UseState(() => selectedPlanState.Value != null
            ? draftDiffCommentService.GetDraftCommentsForPlan(selectedPlanState.Value.FolderPath)
            : new List<DraftComment>());
        var args = UseArgs<ReviewAppArgs>();
        var nav = UseNavigation();
        var planWatcher = UseService<IPlanWatcherService>();
        var localRefresh = UseRefreshToken();
        var githubService = UseService<IGithubService>();
        var agentRunner = UseService<IAgentRunner>();
        var shareContext = UseService<Ivy.Tendril.Services.Share.IShareContext>();
        var shareTunnelService = UseService<Ivy.Tendril.Services.Tunnel.IShareTunnelService>();
        var resetToDraftLogger = UseService<ILogger<ResetToDraftDialog>>();
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);
        Context.TryUseService<IChatHistoryService>(out var chatService);
        Context.TryUseService<IChatExecutionService>(out var chatExecution);

        var processView = Context.UseTendrilProcess();

        var (discardDialog, showDiscardDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new DiscardPlanDialog(isOpen, selectedPlanState.Value!, planService, refreshPlans);
        });

        var (suggestChangesDialog, showSuggestChangesDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new SuggestChangesDialog(isOpen, selectedPlanState.Value!, jobService, refreshPlans, draftComments.Value, draftComments);
        });

        var (shareModal, showShareModal) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new ShareTunnelModal(isOpen, selectedPlanState.Value?.FolderName, isReview: true);
        });

        var (createPrDialog, showCreatePrDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new CreatePrDialog(isOpen, selectedPlanState.Value!, jobService, refreshPlans,
                config, githubService);
        });

        var (resetToDraftDialog, showResetToDraftDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new ResetToDraftDialog(isOpen, selectedPlanState.Value!, planService, refreshPlans,
                resetToDraftLogger);
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

        var artifactContentQuery = UseQuery<string, string>(
            openArtifact.Value ?? "",
            async (filePath, ct) =>
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                if (selectedPlanState.Value is null) return "";
                var artifactsDir = Path.GetFullPath(Path.Combine(selectedPlanState.Value.FolderPath, "Artifacts"));
                var resolvedPath = Path.GetFullPath(filePath);
                if (!resolvedPath.StartsWith(artifactsDir, StringComparison.OrdinalIgnoreCase))
                    return "Access denied: file is outside the artifacts folder.";
                return await Task.Run(() =>
                    File.Exists(resolvedPath) ? FileHelper.ReadAllText(resolvedPath) : "File not found.", ct);
            },
            initialValue: ""
        );

        var planContentQuery = UseQuery<PlanContentData, string>(
            selectedPlanState.Value?.FolderPath,
            async (folderPath, ct) =>
            {
                return await Task.Run(() =>
                {
                    if (selectedPlanState.Value is null)
                        return new PlanContentData(new List<RecommendationYaml>(), null,
                            new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(),
                            new Dictionary<string, bool>(), new List<(string Name, bool ConditionMet)>(), null,
                            new GitTabDataBuilder.GitTabData([], []));

                    // Recommendations from database (or plan.yaml fallback)
                    List<RecommendationYaml> recs;
                    try
                    {
                        recs = planService.GetRecommendationsForPlan(selectedPlanState.Value.FolderName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to get recommendations for {FolderPath}", folderPath);
                        recs = new List<RecommendationYaml>();
                    }

                    // Summary
                    var summPath = Path.Combine(folderPath, "Artifacts", "summary.md");
                    var summaryMd = File.Exists(summPath) ? FileHelper.ReadAllText(summPath) : null;

                    // Artifacts
                    var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

                    // Commit rows
                    var commitRows = PlanContentHelpers.BuildCommitRows(selectedPlanState.Value!, config, gitService);

                    // Git tab data (computed asynchronously in background)
                    var gitData = GitTabDataBuilder.BuildGitTabData(commitRows, selectedPlanState.Value!, config, gitService);

                    // All changes data
                    var allChanges = PlanContentHelpers.GetAllChangesData(selectedPlanState.Value!, config, gitService);

                    // Verification report existence
                    var verReports = selectedPlanState.Value.Verifications.ToDictionary(
                        v => v.Name,
                        v => File.Exists(Path.Combine(folderPath, "Verification", $"{v.Name}.md")));

                    // Review action conditions
                    var projectConfig = config.GetProject(selectedPlanState.Value.Project);
                    var reviewActions = projectConfig?.ReviewActions ?? [];
                    var actionStates = new (string Name, bool ConditionMet)[reviewActions.Count];
                    Parallel.For(0, reviewActions.Count, i =>
                    {
                        var action = reviewActions[i];
                        if (string.IsNullOrEmpty(action.Condition))
                        {
                            actionStates[i] = (action.Name, true);
                            return;
                        }
                        actionStates[i] = (action.Name, PlatformHelper.EvaluatePowerShellCondition(action.Condition, folderPath, logger: logger));
                    });

                    return new PlanContentData(recs, summaryMd, artifacts, commitRows, verReports, actionStates.ToList(), allChanges, gitData);
                }, ct);
            },
            options: QueryScope.View,
            initialValue: new PlanContentData(new List<RecommendationYaml>(), null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(),
                new List<(string Name, bool ConditionMet)>(), null, new GitTabDataBuilder.GitTabData([], []))
        );

        UseEffect(() =>
        {
            void OnChanged(string? _) => localRefresh.Refresh();
            planWatcher.PlansChanged += OnChanged;
            return Disposable.Create(() => planWatcher.PlansChanged -= OnChanged);
        });

        UseEffect(() =>
        {
            if (localRefresh.IsRefreshed)
                planContentQuery.Mutator.Revalidate();
            return Disposable.Empty;
        }, [localRefresh]);

        UseEffect(() => { selectedRecTitles.Set(new HashSet<string>()); return Disposable.Empty; },
            selectedPlanState);

        UseEffect(() =>
        {
            var loaded = selectedPlanState.Value != null
                ? draftDiffCommentService.GetDraftCommentsForPlan(selectedPlanState.Value.FolderPath)
                : new List<DraftComment>();
            draftComments.Set(loaded);
            return Disposable.Empty;
        }, selectedPlanState);

        UseEffect(() =>
        {
            void OnCommentsChanged(string folderPath, List<DraftComment> updated)
            {
                if (selectedPlanState.Value != null && folderPath == selectedPlanState.Value.FolderPath)
                {
                    draftComments.Set(updated);
                }
            }

            draftDiffCommentService.CommentsChanged += OnCommentsChanged;
            return Disposable.Create(() => draftDiffCommentService.CommentsChanged -= OnCommentsChanged);
        });

        var isShareMode = shareContext.IsShareMode;
        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);

        // Keyed on the plan's identity, not on the state object: every refresh hands the state a
        // fresh PlanFile instance of the same plan, and that must not throw the reader back to the
        // Summary tab.
        if (!string.Equals(lastPlanFolder.Value, selectedPlanState.Value?.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            lastPlanFolder.Set(selectedPlanState.Value?.FolderName);
            selectedTab.Set(SummaryTab);
        }

        if (selectedPlanState.Value is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("No plans to review", "Completed plans will appear here for review", processView);

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a completed plan to review");
        }

        var selectedPlan = selectedPlanState.Value;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);
        var context = new ReviewViewContext(client, logger, nav, args, copyToClipboard);
        var sheets = new SheetsState(openVerification, openCommit, openFile, openArtifact, artifactContentQuery);

        void ImplementRecommendations() => ImplementSelectedRecommendations(
            selectedPlan, selectedRecTitles, client,
            planContentQuery.Mutator.Revalidate);

        var actions = ReviewActions.Build(new ReviewActionsContext(
            selectedPlan, config, client, logger, nav, agentRunner, shareContext, shareTunnelService,
            planService, chatService, chatExecution, isBeta,
            copyToClipboard, draftComments.Value.Count,
            showResetToDraftDialog, showSuggestChangesDialog, showDiscardDialog, showShareModal));

        if (!isShareMode)
            AddPrimaryAction(actions, selectedPlan, context, showCreatePrDialog, showDiscardDialog);

        Action discussInChat = chatService != null && chatExecution != null
            ? () => PlanChatSessions.Send(chatService, chatExecution, planService, agentRunner, config, selectedPlan,
                PlanChatSessions.DiscussPrompt(selectedPlan))
            : () => ChatLauncher.Open(nav, config,
                $"User wants to discuss the plan {selectedPlan.FolderPath} currently in Review mode.",
                $"#{TendrilAppShell.FormatPlanId(selectedPlan.FolderName)}");

        var page = BuildPage(
            selectedPlan, planContentQuery, selectedTab, sheets,
            syncingWorktrees, selectedRecTitles, context, showDebugJob, showCostJob, draftComments,
            ImplementRecommendations, discussInChat);

        var workspace = actions.ApplyTo(new PlanWorkspace(
                page.Content,
                isShareMode ? null : new PlanChatView(selectedPlan),
                page.Verifications,
                null,
                page.Toolbar)
            .PlanId($"#{selectedPlan.Id}")
            .Title(selectedPlan.Title)
            .Projects(ProjectHelper.BuildProjectBadges(selectedPlan.Project, config))
            .Meta($"{currentIndex + 1}/{allPlans.Count} plans")
            .Source(
                string.IsNullOrEmpty(selectedPlan.SourceUrl) ? null : selectedPlan.SourceUrl,
                selectedPlan.IsPullRequestSource ? "PR" : "Issue")
            .Persona(
                isShareMode ? shareContext.Persona : null,
                isShareMode ? Ivy.Tendril.Services.Share.AnonymousPersonaGenerator.GetInitials(shareContext.Persona) : null)
            .Tabs(page.Tabs)
            .SelectedTab(page.SelectedTab)
            .OnTabSelect(id => selectedTab.Set(id)))
            .WithLayout().Full().RemoveParentPadding()
            .Key(selectedPlan.Id);

        var elements = new List<object> { workspace };
        elements.AddRange(page.Overlays);
        elements.AddRange([discardDialog, suggestChangesDialog, createPrDialog, resetToDraftDialog, debugSheet, costSheet]);
        if (isBeta || isShareMode)
            elements.Add(shareModal);

        return new Fragment(elements.ToArray());
    }

    private void AddPrimaryAction(
        PlanWorkspaceActions actions,
        PlanFile selectedPlan,
        ReviewViewContext context,
        Action showCreatePrDialog,
        Action showDiscardDialog)
    {
        if (selectedPlan.Commits.Count > 0)
        {
            // When the plan's source is an existing PR, the CTA updates that PR instead of
            // opening a second one. There's nothing to configure for an update (no new branch,
            // no merge/delete choices), so we skip the Create PR dialog and push directly.
            var isPrUpdate = selectedPlan.IsPullRequestSource;
            actions.SetPrimary("CreatePr", isPrUpdate ? "Update PR" : "Create PR", Icons.GitPullRequest, () =>
            {
                if (isPrUpdate)
                {
                    // Push the fix onto the original PR's branch and leave the PR open for
                    // review. ExecutePlan already based the worktree on the PR's head branch,
                    // so CreatePr's push updates the existing PR (no new PR is created).
                    jobService.StartJob(new CreatePrArgs(
                        selectedPlan.FolderPath,
                        SolveMergeConflicts: true,
                        Merge: false,
                        DeleteBranch: false,
                        IncludeArtifacts: true));
                    refreshPlans();
                }
                else
                {
                    showCreatePrDialog();
                }
            }, "m");
            return;
        }

        var completionBlockReason = planService.GetCompletionBlockReason(selectedPlan.FolderName);
        if (completionBlockReason != null)
        {
            actions.SetPrimary("SkipPlan", "Skip Plan", Icons.Ban, showDiscardDialog, "m");
            return;
        }

        actions.SetPrimary("CompletePlan", "Complete Plan", Icons.CircleCheck, () =>
        {
            try
            {
                // Optimistic UI - update state and refresh immediately
                planService.TransitionState(selectedPlan.FolderName, PlanStatus.Completed);
            }
            catch (PlanTransitionBlockedException ex)
            {
                // This handler is fire-and-forget, so an uncaught throw would look like a
                // silent no-op. Surface the reason and leave the plan where it is.
                context.Client.Toast(ex.Message, "Cannot Complete Plan", variant: ToastVariant.Destructive);
                return;
            }

            refreshPlans();

            // Fire and forget - clean up worktrees in the background
            WorktreeCleanupService.RemoveWorktreesInBackground(selectedPlan.FolderPath);
        }, "m");
    }

    private ReviewPage BuildPage(
        PlanFile selectedPlan,
        QueryResult<PlanContentData> planContentQuery,
        IState<string> selectedTab,
        SheetsState sheets,
        IState<HashSet<string>> syncingWorktrees,
        IState<HashSet<string>> selectedRecTitles,
        ReviewViewContext context,
        Action<string> showDebugJob,
        Action<string> showCostJob,
        IState<List<DraftComment>> draftComments,
        Action onImplementRecommendations,
        Action onDiscussWithAgent)
    {
        var (client, logger, nav, args, copyToClipboard) = context;
        var (openVerification, openCommit, openFile, openArtifact, artifactContentQuery) = sheets;

        var overlays = new List<object>
        {
            new VerificationReportSheet(openVerification, selectedPlan, config),
            new CommitDetailSheet(openCommit, selectedPlan, config, gitService)
        };

        if (openArtifact.Value is { } artifactPath)
        {
            var language = FileHelper.GetLanguage(Path.GetExtension(artifactPath));
            overlays.Add(new Sheet(
                () => openArtifact.Set(null),
                artifactContentQuery.Loading
                    ? Text.Muted("Loading...")
                    : artifactContentQuery.Error is { } err
                        ? Text.Muted($"Failed to load artifact: {err.Message}")
                        : new CodeBlock($"{language.ToString().ToLowerInvariant()}\n{artifactContentQuery.Value}\n", Languages.Text),
                Path.GetFileName(artifactPath)
            ).Width(UxHelper.SheetWidth).Resizable());
        }

        overlays.Add(new FileSheet(openFile, config));

        var planData = planContentQuery.Value;
        var tabs = new List<PlanTabDto> { new(SummaryTab, "Summary"), new(PlanTab, "Plan"), new(DetailsTab, "Details"), new(GitTab, "Git") };

        if (planContentQuery.Loading && planData is null)
        {
            return new ReviewPage(tabs, SummaryTab,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()) | Text.Muted("Loading..."),
                null, null, overlays);
        }

        if (planData is null)
        {
            var errorMsg = planContentQuery.Error is { } err
                ? $"Failed to load plan data: {err.Message}"
                : "Failed to load plan data. Please try refreshing.";
            return new ReviewPage(tabs, SummaryTab,
                Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()) | Text.Muted(errorMsg),
                null, null, overlays);
        }

        var pendingRecs = planData.Recommendations.Where(r => r.State == RecommendationStatus.Pending).ToList();

        Action<string> onLinkClick = FileSheet.CreateLinkClickHandler(openFile, planId =>
        {
            var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                .FirstOrDefault();
            if (planFolder != null)
            {
                var plan = planService.GetPlanByFolder(planFolder);
                if (plan != null)
                    selectedPlanState.Set(plan);
            }
        });

        var gitData = planData.GitData ?? new GitTabDataBuilder.GitTabData([], []);
        tabs[3] = tabs[3] with { Badge = GitTabDataBuilder.CountGitItems(gitData, selectedPlan).ToString() };

        var totalArtifacts = (planData.Artifacts.GetValueOrDefault("screenshots")?.Count ?? 0)
                             + (planData.Artifacts.ContainsKey("sample") ? 1 : 0);

        // Only surface the Changes tab once there are actual file changes — no point showing
        // an empty "No commits yet." tab before any work has landed.
        var changesCount = planData.AllChanges?.Files.Count ?? 0;
        if (changesCount > 0)
            tabs.Add(new PlanTabDto(ChangesTab, "Changes", changesCount.ToString()));
        if (totalArtifacts > 0)
            tabs.Add(new PlanTabDto(ArtifactsTab, "Artifacts", totalArtifacts.ToString()));
        if (pendingRecs.Count > 0)
            tabs.Add(new PlanTabDto(RecommendationsTab, "Recommendations", pendingRecs.Count.ToString()));

        // Honor deep-linked tab from URL on initial load
        if (args?.Tab is { } requestedTab && selectedTab.Value == SummaryTab)
        {
            var deepLinked = tabs.FirstOrDefault(t => t.Id.Equals(requestedTab, StringComparison.OrdinalIgnoreCase));
            if (deepLinked != null)
                selectedTab.Set(deepLinked.Id);
        }

        var activeTab = tabs.Any(t => t.Id == selectedTab.Value) ? selectedTab.Value : SummaryTab;

        object content = activeTab switch
        {
            // Summary and Plan are PlanMarkdown, which owns its own scroll, inset and max-width,
            // so neither is wrapped in Cap(): wrapped, each would be inset twice and the two tabs
            // would start their text in different places.
            SummaryTab => new SummaryTabView(config, planData.SummaryMarkdown, onLinkClick, planContentQuery.Loading),
            PlanTab => new PlanTabView(selectedPlan, selectedPlanState, openFile, planService, config),
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
                syncingWorktrees.Value,
                worktreePath => SynchronizeWorktreeAsync(worktreePath, syncingWorktrees, planContentQuery, client, planService, selectedPlanState, logger))),
            // The diff view manages its own scroll, so it gets the workspace inset without Cap().
            ChangesTab => Layout.Vertical().Width(Size.Full()).Height(Size.Full().Min(Size.Px(0))).Padding(8, 2, 8, 0)
                          | new ChangesTabView(
                              planData.AllChanges,
                              planContentQuery.Loading,
                              planContentQuery.Error,
                              draftComments,
                              selectedPlan,
                              jobService,
                              refreshPlans,
                              selectedPlan.Project,
                              onDiscussWithAgent: onDiscussWithAgent),
            ArtifactsTab => Cap(new ArtifactsTabView(planData.Artifacts)),
            RecommendationsTab => Cap(new RecommendationsTabView(pendingRecs, selectedRecTitles, config, onImplementRecommendations, onLinkClick)),
            _ => new SummaryTabView(config, planData.SummaryMarkdown, onLinkClick, planContentQuery.Loading)
        };

        object? toolbar = null;
        var completionBlockReason = planService.GetCompletionBlockReason(selectedPlan.FolderName);
        var hasReviewActions = (config.GetProject(selectedPlan.Project)?.ReviewActions ?? []).Count > 0;
        if (completionBlockReason != null || hasReviewActions)
        {
            var toolbarLayout = Layout.Vertical().Gap(0).Width(Size.Full());
            if (completionBlockReason != null)
            {
                toolbarLayout |= Layout.Vertical().Padding(2, 2, 1, 2)
                    | Callout.Info(
                        "Pre-execution validation found no changes needed because the issue or task is already resolved. You can discard or skip this plan.",
                        "No Changes Needed");
            }

            if (hasReviewActions)
                toolbarLayout |= new ReviewActionsBarView(selectedPlan, planData.ReviewActionStates, config);
            toolbar = toolbarLayout;
        }

        var verificationsPanel = new ReviewVerificationsPanelView(
            selectedPlan.Verifications, planData.VerificationReports, v => openVerification.Set(v));

        return new ReviewPage(tabs, activeTab, content, toolbar, verificationsPanel, overlays);

        // The workspace inset: 24px top, 32px sides, matching what PlanMarkdown applies to itself.
        object Cap(object inner)
        {
            // NOTE: a Responsive<Thickness?> with more than just Default set serializes to a
            // breakpoint OBJECT that the StackLayout frontend drops on the floor (it reads
            // `responsivePadding`, not the `padding` object), so the padding never rendered —
            // which is why earlier spacing fixes here had no visible effect. Use a flat
            // Thickness so it serializes to a plain "L,T,R,B" string the frontend parses.
            return Layout.Vertical().Scroll().HideScrollbar().Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical()
                    .Padding(8, 6, 8, 4)
                    .Width(Size.Full().Max(Size.Units(200))) | inner);
        }
    }

    /// <summary>Everything the workspace shows for the selected plan below its title bar.</summary>
    private record ReviewPage(
        List<PlanTabDto> Tabs,
        string SelectedTab,
        object Content,
        object? Toolbar,
        object? Verifications,
        List<object> Overlays);

    private void ImplementSelectedRecommendations(
        PlanFile selectedPlan,
        IState<HashSet<string>> selectedRecTitles,
        IClientProvider client,
        Action revalidate)
    {
        var titles = selectedRecTitles.Value.ToList();
        if (titles.Count == 0)
        {
            client.Toast("Select at least one recommendation to implement.", "Nothing Selected");
            return;
        }

        var selected = ResolvePendingSelection(
            planService.GetRecommendationsForPlan(selectedPlan.FolderName), titles);
        if (selected.Count == 0)
        {
            client.Toast(
                "Selected recommendations are no longer pending. Refresh and try again.",
                "Nothing to Implement");
            return;
        }

        // Single job for the whole batch, never one StartJob call per recommendation.
        var changeRequest = BuildRecommendationChangeRequest(selected);
        planService.AcceptRecommendationsAndRetry(selectedPlan.FolderName, titles);
        jobService.StartJob(new RetryPlanArgs(selectedPlan.FolderPath, changeRequest));
        var message = selected.Count == 1
            ? "Started RetryPlan for recommendation"
            : $"Started RetryPlan for {selected.Count} recommendations";
        var title = selected.Count == 1 ? "Implementing Recommendation" : "Implementing Recommendations";
        client.Toast(message, title);

        selectedRecTitles.Set(new HashSet<string>());
        refreshPlans();
        revalidate();
    }

    internal static List<RecommendationYaml> ResolvePendingSelection(
        IEnumerable<RecommendationYaml> allRecs, IReadOnlyCollection<string> selectedTitles)
        => allRecs.Where(r => r.State == RecommendationStatus.Pending
                              && selectedTitles.Contains(r.Title)).ToList();

    private static string BuildRecommendationChangeRequest(List<RecommendationYaml> recs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Implement the following {recs.Count} recommendation(s) from the review:");
        sb.AppendLine();
        for (var i = 0; i < recs.Count; i++)
        {
            sb.AppendLine($"## {i + 1}. {recs[i].Title}");
            sb.AppendLine();
            sb.AppendLine(recs[i].Description);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    internal static bool ValidateArtifactPath(string filePath, string planFolderPath)
    {
        var artifactsDir = Path.GetFullPath(Path.Combine(planFolderPath, "Artifacts"));
        var resolvedPath = Path.GetFullPath(filePath);
        return resolvedPath.StartsWith(artifactsDir, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ValidateVerificationPath(string name, string planFolderPath)
    {
        var verificationDir = Path.GetFullPath(Path.Combine(planFolderPath, "Verification"));
        var resolvedPath = Path.GetFullPath(Path.Combine(verificationDir, $"{name}.md"));
        return resolvedPath.StartsWith(verificationDir, StringComparison.OrdinalIgnoreCase);
    }

    private static async void SynchronizeWorktreeAsync(
        string worktreePath,
        IState<HashSet<string>> syncingState,
        QueryResult<PlanContentData> query,
        IClientProvider client,
        IPlanReaderService planService,
        IState<PlanFile?> selectedPlanState,
        ILogger? logger)
    {
        var paths = new HashSet<string>(syncingState.Value) { worktreePath };
        syncingState.Set(paths);

        try
        {
            var (exitCode, error) = await Task.Run(() =>
            {
                var psi = GitHelper.MakeGitStartInfo("fetch origin", worktreePath);
                using var process = Process.Start(psi);
                if (process == null)
                    return (1, "Failed to start git process");

                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(60000);
                return (process.ExitCode, stderr);
            });

            if (exitCode == 0)
            {
                if (WorktreePathHelper.TryGetPlanFolderFromWorktree(worktreePath, out var planFolder))
                {
                    planService.SyncPlanArtifacts(planFolder);
                    var refreshed = planService.GetPlanByFolderFromDisk(planFolder);
                    if (refreshed != null)
                        selectedPlanState.Set(refreshed);
                    client.Toast("Worktree synchronized successfully", "Synchronized");
                }
                else
                {
                    client.Toast("Failed to locate plan folder from worktree path", "Synchronization failed");
                }
            }
            else
            {
                client.Toast($"git fetch failed: {error}", "Synchronize Failed", variant: ToastVariant.Destructive);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to synchronize worktree at {Path}", worktreePath);
            client.Toast($"Failed to synchronize: {ex.Message}", "Synchronize Failed", variant: ToastVariant.Destructive);
        }
        finally
        {
            var updated = new HashSet<string>(syncingState.Value);
            updated.Remove(worktreePath);
            syncingState.Set(updated);
            query.Mutator.Revalidate();
        }
    }

    private record PlanContentData(
        List<RecommendationYaml> Recommendations,
        string? SummaryMarkdown,
        Dictionary<string, List<string>> Artifacts,
        List<PlanContentHelpers.CommitRow> CommitRows,
        Dictionary<string, bool> VerificationReports,
        List<(string Name, bool ConditionMet)> ReviewActionStates,
        PlanContentHelpers.AllChangesData? AllChanges,
        GitTabDataBuilder.GitTabData? GitData);

    // Groups the request-scoped services and navigation state shared by AddPrimaryAction and
    // BuildPage, so a new piece of shared infrastructure only needs to be added here instead of
    // threaded through every signature and call site.
    private record ReviewViewContext(
        IClientProvider Client,
        ILogger<ContentView> Logger,
        INavigator Nav,
        ReviewAppArgs? Args,
        Action<string> CopyToClipboard);

    // Groups the sheet-open state consumed by BuildPage, mirroring PlanContentData's grouping of
    // query results.
    private record SheetsState(
        IState<string?> OpenVerification,
        IState<string?> OpenCommit,
        IState<string?> OpenFile,
        IState<string?> OpenArtifact,
        QueryResult<string> ArtifactContentQuery);
}
