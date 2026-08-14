using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text;
using Ivy.Core;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Agent;
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
        var selectedTab = UseState(0);
        var draftComments = UseState(() => new List<DraftComment>());
        var args = UseArgs<ReviewAppArgs>();
        var nav = UseNavigation();

        var processView = Context.UseTendrilProcess();

        var githubService = UseService<IGithubService>();
        var agentRunner = UseService<IAgentRunner>();
        var assigneesError = UseState<string?>(null);
        var assigneesQuery = UseQuery<string[], string>(
            selectedPlanState.Value?.Project ?? "",
            async (_, _) =>
            {
                if (selectedPlanState.Value is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repos = selectedPlanState.Value.GetEffectiveRepoPaths(config);
                var repoPath = repos.FirstOrDefault();
                if (repoPath is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repoConfig = githubService.GetRepoConfigFromPathCached(repoPath);
                if (repoConfig is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repoConfig.Owner, repoConfig.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

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

        var (createPrDialog, showCreatePrDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new CreatePrDialog(isOpen, selectedPlanState.Value!, jobService, refreshPlans,
                assigneesQuery, assigneesError);
        });

        var resetToDraftLogger = UseService<ILogger<ResetToDraftDialog>>();
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
                new JobCostSheet(jobId, jobService),
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
                            new Dictionary<string, bool>(), new List<(string Name, bool ConditionMet)>(), null);

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

                    return new PlanContentData(recs, summaryMd, artifacts, commitRows, verReports, actionStates.ToList(), allChanges);
                }, ct);
            },
            options: QueryScope.View,
            initialValue: new PlanContentData(new List<RecommendationYaml>(), null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(),
                new List<(string Name, bool ConditionMet)>(), null)
        );

        var planWatcher = UseService<IPlanWatcherService>();
        var localRefresh = UseRefreshToken();

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

        UseEffect(() => { selectedTab.Set(0); return Disposable.Empty; }, selectedPlanState);

        UseEffect(() => { selectedRecTitles.Set(new HashSet<string>()); return Disposable.Empty; },
            selectedPlanState);

        UseEffect(() => { draftComments.Set(new List<DraftComment>()); return Disposable.Empty; }, selectedPlanState);

        if (selectedPlanState.Value is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("No plans to review", "Completed plans will appear here for review", processView);

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a completed plan to review");
        }

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlanState.Value.FolderName);
        var context = new ReviewViewContext(client, logger, nav, args, copyToClipboard);
        var sheets = new SheetsState(openVerification, openCommit, openFile, openArtifact, artifactContentQuery);

        void ImplementRecommendations() => ImplementSelectedRecommendations(
            selectedPlanState.Value!, selectedRecTitles, client,
            planContentQuery.Mutator.Revalidate);

        var header = BuildHeader(selectedPlanState.Value, allPlans, currentIndex, context, showCreatePrDialog);
        var actionBar = BuildActionBar(
            selectedPlanState.Value, showResetToDraftDialog, showSuggestChangesDialog, showDiscardDialog,
            showCreatePrDialog, context, agentRunner, draftComments);
        var content = BuildContent(
            selectedPlanState.Value, planContentQuery, selectedTab, sheets,
            syncingWorktrees, selectedRecTitles, context, showDebugJob, showCostJob, draftComments,
            ImplementRecommendations);

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Scroll(Scroll.None).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlanState.Value.Id);

        return new Fragment(mainLayout, discardDialog, suggestChangesDialog, createPrDialog, resetToDraftDialog,
            debugSheet, costSheet);
    }

    private object BuildHeader(
        PlanFile selectedPlan,
        List<PlanFile> allPlans,
        int currentIndex,
        ReviewViewContext context,
        Action showCreatePrDialog)
    {
        object BuildTitleArea(bool isMobile)
        {
            object SourceButton() => new Button(selectedPlan.IsPullRequestSource ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => context.Client.OpenUrl(selectedPlan.SourceUrl));

            var hasSourceUrl = !string.IsNullOrEmpty(selectedPlan.SourceUrl);

            var desktopTitleLayout = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full().Min(Size.Px(0)))
                | Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis)
                    .Width(Size.Shrink().Min(Size.Px(0)));

            if (hasSourceUrl)
                desktopTitleLayout |= SourceButton();

            var desktopTitle = new Box(desktopTitleLayout).BorderThickness(0).Padding(0)
                .Width(Size.Full().Min(Size.Px(0)))
                .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

            var mobileTitleLayout = Layout.Horizontal().Gap(2).AlignContent(Align.Left).Width(Size.Full())
                | MobileItemPicker.Build(
                        $"#{selectedPlan.Id} {selectedPlan.Title}",
                        allPlans,
                        p => $"#{p.Id} {p.Title}",
                        p => p.FolderName == selectedPlan.FolderName,
                        p => selectedPlanState.Set(p))
                    .Width(Size.Grow().Min(Size.Px(0)));

            if (hasSourceUrl)
                mobileTitleLayout |= SourceButton();

            var mobileTitle = new Box(mobileTitleLayout).BorderThickness(0).Padding(0)
                .Width(Size.Full().Min(Size.Px(0)))
                .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

            return Layout.Vertical().Gap(1).AlignContent(Align.Left).Width(Size.Grow().Min(Size.Px(0)))
                   | desktopTitle
                   | mobileTitle;
        }

        object BuildControls(bool isMobile)
        {
            var rightSide = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
                           | Text.Rich()
                               .NoWrap()
                               .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                               .Muted("plans", word: true);

            if (selectedPlan.Commits.Count > 0)
            {
                // When the plan's source is an existing PR, the CTA updates that PR instead of
                // opening a second one. There's nothing to configure for an update (no new branch,
                // no merge/delete choices), so we skip the Create PR dialog and push directly.
                var isPrUpdate = selectedPlan.IsPullRequestSource;

                var createPrBtn = new Button(isPrUpdate ? "Update PR" : "Create PR")
                    .Icon(Icons.GitPullRequest).OnClick(() =>
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
                }).ShortcutKey("m");
                createPrBtn = createPrBtn.Primary();

                rightSide |= createPrBtn;
            }
            else
            {
                var completePlanBtn = new Button("Complete Plan").Icon(Icons.CircleCheck).OnClick(() =>
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
                }).ShortcutKey("m");
                rightSide |= completePlanBtn.Primary();
            }

            return rightSide.Width(isMobile ? Size.Full() : Size.Fit());
        }

        return ResponsiveHeader.Build(BuildTitleArea, BuildControls);
    }

    private object BuildActionBar(
        PlanFile selectedPlan,
        Action showResetToDraftDialog,
        Action showSuggestChangesDialog,
        Action showDiscardDialog,
        Action showCreatePrDialog,
        ReviewViewContext context,
        IAgentRunner agentRunner,
        IState<List<DraftComment>> draftComments)
    {
        var (client, logger, nav, args, copyToClipboard) = context;
        var (agentLabel, agentIcon) = AgentBranding.For(config.Settings.CodingAgent, agentRunner, config);

        // Standard overflow menu items
        var standardOverflowItems = new[]
        {
            new MenuItem($"Discuss with {agentLabel}", Icon: agentIcon, Tag: "DiscussWithAgent")
                .OnSelect(() => nav.Navigate<AgentApp>(new AgentAppArgs(
                    $"User wants to discuss the plan {selectedPlan.FolderPath} currently in Review mode.",
                    $"#{TendrilAppShell.FormatPlanId(selectedPlan.FolderName)}"))),
            new MenuItem("Create PR", Icon: Icons.GitPullRequest, Tag: "CreatePR").OnSelect(showCreatePrDialog),
            new MenuItem("Set Completed", Icon: Icons.CircleCheck, Tag: "SetCompleted").OnSelect(() =>
            {
                try
                {
                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Completed);
                }
                catch (PlanTransitionBlockedException ex)
                {
                    client.Toast(ex.Message, "Cannot Complete Plan", variant: ToastVariant.Destructive);
                    return;
                }

                refreshPlans();
            }),
            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                .OnSelect(() => { PlatformHelper.OpenInFileManager(selectedPlan.FolderPath, logger); }),
            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
            {
                PlatformHelper.OpenInTerminal(selectedPlan.FolderPath, logger);
            }),
            new MenuItem("Copy Path", Icon: Icons.Copy, Tag: "CopyPath")
                .OnSelect(() =>
                {
                    copyToClipboard(selectedPlan.FolderPath);
                    client.Toast("Copied path to clipboard", "Path Copied");
                }),
            new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                .OnSelect(() =>
                {
                    try
                    {
                        config.OpenInEditor(selectedPlan.FolderPath);
                    }
                    catch (EditorNotAvailableException ex)
                    {
                        client.Toast(
                            $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                            "Editor Not Available",
                            variant: ToastVariant.Destructive);
                    }
                }),
            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
            {
                var yamlPath = Path.Combine(selectedPlan.FolderPath, "plan.yaml");
                try
                {
                    config.OpenInEditor(yamlPath);
                }
                catch (EditorNotAvailableException ex)
                {
                    client.Toast(
                        $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                        "Editor Not Available",
                        variant: ToastVariant.Destructive);
                }
            })
        };

        // Full-tier dropdown: standard overflow items only (all buttons shown inline)
        var fullDropdownItems = standardOverflowItems;

        // Compact-tier dropdown: Discard + standard overflow
        var compactDropdownItems = new List<MenuItem>
        {
            new MenuItem("Discard", Icon: Icons.Trash, Tag: "Discard").OnSelect(showDiscardDialog)
        };
        compactDropdownItems.AddRange(standardOverflowItems);

        var commentCount = draftComments.Value.Count;
        var requestChangesMenuLabel = commentCount > 0 ? $"Request Changes ({commentCount})" : "Request Changes";

        // Minimal-tier dropdown: all action buttons + standard overflow
        var minimalDropdownItems = new List<MenuItem>
        {
            new MenuItem("Reset to Draft", Icon: Icons.RotateCcw, Tag: "ResetToDraft").OnSelect(showResetToDraftDialog),
            new MenuItem(requestChangesMenuLabel, Icon: Icons.MessageSquare, Tag: "RequestChanges").OnSelect(showSuggestChangesDialog),
            new MenuItem("Discard", Icon: Icons.Trash, Tag: "Discard").OnSelect(showDiscardDialog)
        };
        minimalDropdownItems.AddRange(standardOverflowItems);

        var requestChangesBtn = new Button("Request Changes")
            .Icon(Icons.MessageSquare)
            .ShortcutKey("c")
            .OnClick(showSuggestChangesDialog)
            .CompactUp();

        if (commentCount > 0)
        {
            requestChangesBtn = requestChangesBtn.Badge(commentCount.ToString()).Primary();
        }
        else
        {
            requestChangesBtn = requestChangesBtn.Outline();
        }

        // Action bar without .Wrap() - single row with progressive collapse.
        // Full (>=1024px): Previous, Next, Reset to Draft, Request Changes, Discard inline + overflow dropdown.
        // Compact (768-1023px): Previous, Next, Reset to Draft, Request Changes inline; Discard in dropdown.
        // Minimal (<768px): Previous, Next inline; everything else in dropdown.
        return Layout.Horizontal().AlignContent(Align.Left).Gap(2)
                | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious(nav, args))
                    .ShortcutKey("p").AlwaysVisible()
                | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext(nav, args))
                    .ShortcutKey("n").AlwaysVisible()
                | new Button("Reset to Draft").Icon(Icons.RotateCcw).Outline().ShortcutKey("r")
                    .OnClick(showResetToDraftDialog).CompactUp()
                | requestChangesBtn
                | new Button("Discard").Icon(Icons.Trash).Outline().ShortcutKey("Backspace")
                    .OnClick(showDiscardDialog).FullOnly()
                | ActionBarResponsive.DropdownAtFull(
                    new Button().Icon(Icons.EllipsisVertical).Ghost(),
                    fullDropdownItems)
                | ActionBarResponsive.DropdownAtCompact(
                    new Button().Icon(Icons.EllipsisVertical).Ghost(),
                    compactDropdownItems.ToArray())
                | ActionBarResponsive.DropdownAtMinimal(
                    new Button().Icon(Icons.EllipsisVertical).Ghost(),
                    minimalDropdownItems.ToArray());
    }

    private object BuildContent(
        PlanFile selectedPlan,
        QueryResult<PlanContentData> planContentQuery,
        IState<int> selectedTab,
        SheetsState sheets,
        IState<HashSet<string>> syncingWorktrees,
        IState<HashSet<string>> selectedRecTitles,
        ReviewViewContext context,
        Action<string> showDebugJob,
        Action<string> showCostJob,
        IState<List<DraftComment>> draftComments,
        Action onImplementRecommendations)
    {
        var (client, logger, nav, args, copyToClipboard) = context;
        var (openVerification, openCommit, openFile, openArtifact, artifactContentQuery) = sheets;
        var content = Layout.Vertical().Gap(0).Height(Size.Full());

        if (selectedPlan is null)
        {
            return content | Text.Muted("No plan selected");
        }

        var planData = planContentQuery.Value;
        var pendingRecs = planData.Recommendations.Where(r => r.State == RecommendationStatus.Pending).ToList();

        var planTabContent = new PlanTabView(selectedPlan, selectedPlanState, openFile, planService, config);

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

        if (planContentQuery.Loading && planData is null)
        {
            content |= Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                       | Text.Muted("Loading...");
        }
        else if (planData is null)
        {
            var errorMsg = planContentQuery.Error is { } err
                ? $"Failed to load plan data: {err.Message}"
                : "Failed to load plan data. Please try refreshing.";
            content |= Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                       | Text.Muted(errorMsg);
        }
        else
        {
            var gitData = GitTabDataBuilder.BuildGitTabData(planData.CommitRows, selectedPlan!, config, gitService);
            var gitTabView = new GitTabView(
                gitData,
                selectedPlan!,
                hash => openCommit.Set(hash),
                path =>
                {
                    copyToClipboard(path);
                    client.Toast("Copied path to clipboard", "Path Copied");
                    return null!;
                },
                syncingWorktrees.Value,
                worktreePath => SynchronizeWorktreeAsync(worktreePath, syncingWorktrees, planContentQuery, client, planService, selectedPlanState, logger)
            );

            var totalArtifacts = (planData.Artifacts.GetValueOrDefault("screenshots")?.Count ?? 0)
                                 + (planData.Artifacts.ContainsKey("sample") ? 1 : 0);

            content |= new ReviewActionsBarView(selectedPlan, planData.ReviewActionStates, config);

            var recommendationsTab = new RecommendationsTabView(pendingRecs, selectedRecTitles, config, onImplementRecommendations, onLinkClick);

            var changesTabView = new ChangesTabView(
                planData.AllChanges,
                planContentQuery.Loading,
                planContentQuery.Error,
                draftComments,
                selectedPlan!,
                jobService,
                refreshPlans,
                selectedPlan.Project,
                onDiscussWithAgent: () => nav.Navigate<AgentApp>(new AgentAppArgs(
                    $"User wants to discuss the plan {selectedPlan.FolderPath} currently in Review mode.",
                    $"#{TendrilAppShell.FormatPlanId(selectedPlan.FolderName)}")));

            var tabNamesList = new List<string> { "summary", "plan", "details", "git" };
            var tabList = new List<Tab>
            {
                // Summary is rendered via DraftMarkdown with a pinned Verifications sidebar, so it is
                // NOT wrapped in Cap() (whose outer scroll would also scroll the sticky box). The widget
                // reproduces Cap()'s left inset + max-width.
                new Tab("Summary", new SummaryTabView(
                    config, planData.SummaryMarkdown, selectedPlan.Verifications,
                    planData.VerificationReports, v => openVerification.Set(v), onLinkClick,
                    planContentQuery.Loading)),
                new Tab("Plan", Cap(planTabContent)),
                new Tab("Details", Cap(new DetailsTabView(selectedPlan,
                    jobService.GetJobsForPlan(selectedPlan.FolderName),
                    showDebugJob, showCostJob, planService, selectedPlanState, refreshPlans,
                    folderPath => selectedPlanState.Set(planService.GetPlanByFolder(folderPath))))),
                new Tab("Git", Cap(gitTabView)).Badge(GitTabDataBuilder.CountGitItems(gitData, selectedPlan).ToString()),
            };

            // Only surface the Changes tab once there are actual file changes — no point showing
            // an empty "No commits yet." tab before any work has landed.
            if (changesTabView.FileCount > 0)
            {
                tabList.Add(new Tab("Changes",
                        Layout.Vertical().Width(Size.Full()).Height(Size.Full().Min(Size.Px(0))) | changesTabView)
                    .Badge(changesTabView.FileCount.ToString()));
                tabNamesList.Add("changes");
            }

            if (totalArtifacts > 0)
            {
                tabList.Add(new Tab("Artifacts", Cap(new ArtifactsTabView(planData.Artifacts))).Badge(totalArtifacts.ToString()));
                tabNamesList.Add("Artifacts");
            }

            if (pendingRecs.Count > 0)
            {
                tabList.Add(new Tab("Recommendations", Cap(recommendationsTab)).Badge(pendingRecs.Count.ToString()));
                tabNamesList.Add("recommendations");
            }

            var actualTabNames = tabNamesList.ToArray();

            // Honor deep-linked tab from URL on initial load
            if (args?.Tab is { } requestedTab && selectedTab.Value == 0)
            {
                var deepLinkIndex = Array.IndexOf(actualTabNames, requestedTab);
                if (deepLinkIndex >= 0)
                    selectedTab.Set(deepLinkIndex);
            }

            var tabs = Layout.Tabs(tabList.ToArray())
                .OnSelect(v => selectedTab.Set(v))
                .SelectedIndex(selectedTab.Value)
                .Variant(TabsVariant.Content)
                .RemoveParentPadding();

            content |= (Layout.Vertical().Padding(0, 2, 2, 2).Height(Size.Full()) | tabs);
        }

        content |= new VerificationReportSheet(openVerification, selectedPlan, config);
        content |= new CommitDetailSheet(openCommit, selectedPlan, config, gitService);

        if (openArtifact.Value is { } artifactPath)
        {
            var language = FileHelper.GetLanguage(Path.GetExtension(artifactPath));
            content |= new Sheet(
                () => openArtifact.Set(null),
                artifactContentQuery.Loading
                    ? Text.Muted("Loading...")
                    : artifactContentQuery.Error is { } err
                        ? Text.Muted($"Failed to load artifact: {err.Message}")
                        : new CodeBlock($"{language.ToString().ToLowerInvariant()}\n{artifactContentQuery.Value}\n", Languages.Text),
                Path.GetFileName(artifactPath)
            ).Width(UxHelper.SheetWidth).Resizable();
        }

        content |= new FileSheet(openFile, config);

        return content;

        object Cap(object inner)
        {
            // NOTE: a Responsive<Thickness?> with more than just Default set serializes to a
            // breakpoint OBJECT that the StackLayout frontend drops on the floor (it reads
            // `responsivePadding`, not the `padding` object), so the padding never rendered —
            // which is why earlier spacing fixes here had no visible effect. Use a flat
            // Thickness so it serializes to a plain "L,T,R,B" string the frontend parses.
            // Left = 8 units (32px) gives the markdown the left gutter requested in #1252.
            return Layout.Vertical().Scroll().HideScrollbar().Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical()
                    .Padding(8, 2, 0, 4)
                    .Width(Size.Full().Max(Size.Units(200))) | inner);
        }
    }

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

    private void GoToNext(INavigator nav, ReviewAppArgs? args)
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlanState.Value?.FolderName);
        var nextIndex = (currentIndex + 1) % allPlans.Count;
        selectedPlanState.Set(allPlans[nextIndex]);
    }

    private void GoToPrevious(INavigator nav, ReviewAppArgs? args)
    {
        if (allPlans.Count == 0) return;
        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlanState.Value?.FolderName);
        var prevIndex = (currentIndex - 1 + allPlans.Count) % allPlans.Count;
        selectedPlanState.Set(allPlans[prevIndex]);
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
                var planFolder = Path.GetFullPath(Path.Combine(worktreePath, "..", ".."));
                planService.SyncPlanArtifacts(planFolder);
                var refreshed = planService.GetPlanByFolderFromDisk(planFolder);
                if (refreshed != null)
                    selectedPlanState.Set(refreshed);
                client.Toast("Worktree synchronized successfully", "Synchronized");
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
        PlanContentHelpers.AllChangesData? AllChanges);

    // Groups the request-scoped services and navigation state shared by BuildHeader, BuildActionBar
    // and BuildContent, so a new piece of shared infrastructure only needs to be added here instead
    // of threaded through every Build* signature and call site.
    private record ReviewViewContext(
        IClientProvider Client,
        ILogger<ContentView> Logger,
        INavigator Nav,
        ReviewAppArgs? Args,
        Action<string> CopyToClipboard);

    // Groups the sheet-open state consumed by BuildContent, mirroring PlanContentData's grouping of
    // query results.
    private record SheetsState(
        IState<string?> OpenVerification,
        IState<string?> OpenCommit,
        IState<string?> OpenFile,
        IState<string?> OpenArtifact,
        QueryResult<string> ArtifactContentQuery);
}
