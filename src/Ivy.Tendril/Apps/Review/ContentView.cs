using System.Diagnostics;
using System.Reactive.Disposables;
using Ivy.Core;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Apps.Views.Tabs;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

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
        var args = UseArgs<ReviewAppArgs>();
        var nav = UseNavigation();

        var processView = Context.UseTendrilProcessView();

        var githubService = UseService<IGithubService>();
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
            return new SuggestChangesDialog(isOpen, selectedPlanState.Value!, jobService, planService, refreshPlans);
        });

        var (customPrDialog, showCustomPrDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new CustomPrDialog(isOpen, selectedPlanState.Value!, jobService, planService, refreshPlans,
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
                new JobDebugSheet(jobId, jobService, planService, config),
                "Job Debug"
            ).Width(Size.Half()).Resizable();
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

                    // Recommendations from plan.yaml
                    List<RecommendationYaml> recs;
                    try
                    {
                        var planYamlPath = Path.Combine(folderPath, "plan.yaml");
                        if (File.Exists(planYamlPath))
                        {
                            var yaml = FileHelper.ReadAllText(planYamlPath);
                            var planYaml = YamlHelper.Deserializer.Deserialize<PlanYaml>(yaml);
                            recs = planYaml?.Recommendations ?? new List<RecommendationYaml>();
                        }
                        else
                        {
                            recs = new List<RecommendationYaml>();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to parse recommendations from plan.yaml for {FolderPath}", folderPath);
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

        var tabNames = new[] { "summary", "plan", "details", "verifications", "git", "changes", "Artifacts", "recommendations" };
        var selectedTabIndex = Array.IndexOf(tabNames, args?.Tab ?? "summary");
        if (selectedTabIndex < 0) selectedTabIndex = 0;

        if (selectedPlanState.Value is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("No plans to review", "Completed plans will appear here for review.", processView);

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a completed plan to review");
        }

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlanState.Value.FolderName);
        var planData = planContentQuery.Value;

        var header = BuildHeader(selectedPlanState.Value, allPlans, currentIndex, client, showCustomPrDialog, nav, args);
        var actionBar = BuildActionBar(
            selectedPlanState.Value, showResetToDraftDialog, showSuggestChangesDialog, showDiscardDialog,
            showCustomPrDialog, copyToClipboard, client, logger, nav, args);
        var content = BuildContent(
            selectedPlanState.Value, planData, planContentQuery, selectedTabIndex, tabNames, openVerification,
            openCommit, openFile, openArtifact, artifactContentQuery, assigneesQuery,
            assigneesError, syncingWorktrees,
            client, copyToClipboard, logger, nav, args, showDebugJob);

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Scroll(Scroll.None).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlanState.Value.Id);

        return new Fragment(mainLayout, discardDialog, suggestChangesDialog, customPrDialog, resetToDraftDialog, debugSheet);
    }

    private object BuildHeader(
        PlanFile selectedPlan,
        List<PlanFile> allPlans,
        int currentIndex,
        IClientProvider client,
        Action showCustomPrDialog,
        INavigator nav,
        ReviewAppArgs? args)
    {
        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis);

        if (!string.IsNullOrEmpty(selectedPlan.SourceUrl))
            header |= new Button(selectedPlan.SourceUrl.Contains("/pull/") ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => client.OpenUrl(selectedPlan.SourceUrl));

        header |= new Spacer().Width(Size.Grow());

        header |= Text.Rich()
                         .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                         .Muted("plans", word: true);

        var repoPaths = selectedPlan.GetEffectiveRepoPaths(config);
        var project = config.GetProject(selectedPlan.Project);
        var allYolo = repoPaths.All(rp =>
            project?.GetRepoRef(rp)?.PrRule == "yolo");

        var createPrBtn = new Button("Create PR").Icon(Icons.GitPullRequest).Primary().OnClick(() =>
        {
            if (allYolo)
            {
                jobService.StartJob(new CreatePrArgs(selectedPlan.FolderPath, SolveMergeConflicts: true));
                planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
                refreshPlans();
            }
            else
            {
                showCustomPrDialog();
            }
        }).ShortcutKey("m");

        header |= allYolo
            ? createPrBtn.WithConfetti(AnimationTrigger.Click)
            : createPrBtn;

        return header;
    }

    private object BuildActionBar(
        PlanFile selectedPlan,
        Action showResetToDraftDialog,
        Action showSuggestChangesDialog,
        Action showDiscardDialog,
        Action showCustomPrDialog,
        Action<string> copyToClipboard,
        IClientProvider client,
        ILogger<ContentView> logger,
        INavigator nav,
        ReviewAppArgs? args)
    {
        return Layout.Horizontal().AlignContent(Align.Left).Gap(2)
                | new Button("Reset to Draft").Icon(Icons.RotateCcw).Outline().ShortcutKey("r").OnClick(showResetToDraftDialog)
                | new Button("Suggest Changes").Icon(Icons.MessageSquare).Outline().OnClick(showSuggestChangesDialog).ShortcutKey("d")
                | new Button("Discard").Icon(Icons.Trash).Outline().ShortcutKey("Backspace").OnClick(showDiscardDialog)
                | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious(nav, args))
                    .ShortcutKey("p")
                | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext(nav, args))
                    .ShortcutKey("n")
                | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                    new MenuItem("Custom PR", Icon: Icons.GitPullRequest, Tag: "CustomPR").OnSelect(showCustomPrDialog),
                    new MenuItem("Set Completed", Icon: Icons.CircleCheck, Tag: "SetCompleted").OnSelect(() =>
                    {
                        planService.TransitionState(selectedPlan.FolderName, PlanStatus.Completed);
                        refreshPlans();
                    }),
                    new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                        .OnSelect(() => { PlatformHelper.OpenInFileManager(selectedPlan.FolderPath, logger); }),
                    new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
                    {
                        PlatformHelper.OpenInTerminal(selectedPlan.FolderPath, logger);
                    }),
                    new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
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
                );
    }

    private object BuildContent(
        PlanFile selectedPlan,
        PlanContentData planData,
        QueryResult<PlanContentData> planContentQuery,
        int selectedTabIndex,
        string[] tabNames,
        IState<string?> openVerification,
        IState<string?> openCommit,
        IState<string?> openFile,
        IState<string?> openArtifact,
        QueryResult<string> artifactContentQuery,
        QueryResult<string[]> assigneesQuery,
        IState<string?> assigneesError,
        IState<HashSet<string>> syncingWorktrees,
        IClientProvider client,
        Action<string> copyToClipboard,
        ILogger<ContentView> logger,
        INavigator nav,
        ReviewAppArgs? args,
        Action<string> showDebugJob)
    {
        var content = Layout.Vertical().Height(Size.Full()).Gap(0);

        if (selectedPlan is null)
        {
            return content | Text.Muted("No plan selected");
        }

        var reviewAnnotated = MarkdownHelper.AnnotateAllBrokenLinks(selectedPlan.LatestRevisionContent, planService.PlansDirectory);
        var planTabContent = Layout.Vertical().Height(Size.Full())
            | new Markdown(reviewAnnotated)
                .DangerouslyAllowLocalFiles()
                .Article()
                .OnLinkClick(FileSheet.CreateLinkClickHandler(openFile, planId =>
                {
                    var planFolder = Directory.GetDirectories(planService.PlansDirectory, $"{planId:D5}-*")
                        .FirstOrDefault();
                    if (planFolder != null)
                    {
                        var plan = planService.GetPlanByFolder(planFolder);
                        if (plan != null)
                            selectedPlanState.Set(plan);
                    }
                }));

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

            var reviewActionStates = planData.ReviewActionStates;
            var projectConfig = config.GetProject(selectedPlan.Project);
            var reviewActions = projectConfig?.ReviewActions ?? new List<ReviewActionConfig>();
            if (reviewActions.Count > 0)
            {
                var actionsBar = Layout.Horizontal().Gap(2).Padding(2).Height(Size.Fit());
                for (var i = 0; i < reviewActions.Count; i++)
                {
                    var action = reviewActions[i];
                    var conditionMet = i < reviewActionStates.Count && reviewActionStates[i].ConditionMet;

                    var btn = new Button(action.Name).Icon(Icons.Play).Outline();
                    if (!conditionMet)
                    {
                        btn = btn.Disabled();
                    }
                    else
                    {
                        var actionCapture = action;
                        btn = btn.OnClick(() =>
                        {
                            if (!PlatformHelper.RunPowerShellAction(actionCapture.Command, selectedPlan.FolderPath, logger))
                            {
                                logger.LogWarning("Failed to run review action {ActionName}: pwsh not found", actionCapture.Name);
                            }
                        });
                    }

                    actionsBar |= btn;
                }

                content |= actionsBar;
            }

            var pendingRecs = planData.Recommendations.Where(r => r.State == RecommendationStatus.Pending).ToList();
            var recommendationsLayout = Layout.Vertical().Padding(2);
            if (pendingRecs.Count == 0)
                recommendationsLayout |= Text.Muted("No recommendations.");
            else
                foreach (var rec in pendingRecs)
                {
                    var titleRow = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
                                   | Text.Block(rec.Title).Bold();

                    if (rec.Impact is { } impact)
                        titleRow |= new Badge($"Impact: {impact}").Variant(impact switch
                        {
                            "High" => BadgeVariant.Success,
                            "Medium" => BadgeVariant.Warning,
                            _ => BadgeVariant.Outline
                        });

                    if (rec.Risk is { } risk)
                        titleRow |= new Badge($"Risk: {risk}").Variant(risk switch
                        {
                            "High" => BadgeVariant.Destructive,
                            "Medium" => BadgeVariant.Warning,
                            _ => BadgeVariant.Success
                        });

                    var card = Layout.Vertical().Gap(1)
                               | titleRow
                               | new Markdown(rec.Description).DangerouslyAllowLocalFiles().Article();

                    var recTitle = rec.Title;
                    var buttonRow = Layout.Horizontal().Gap(1)
                                    | new Button("Accept").Icon(Icons.Check).Primary().OnClick(() =>
                                    {
                                        planService.ResetVerificationsForRetry(selectedPlan.FolderName);
                                        planService.TransitionState(selectedPlan.FolderName, PlanStatus.Executing);
                                        planService.UpdateRecommendationState(selectedPlan.FolderName, recTitle, RecommendationStatus.Accepted);
                                        jobService.StartJob(new RetryPlanArgs(selectedPlan.FolderPath, rec.Description));
                                        refreshPlans();
                                        planContentQuery.Mutator.Revalidate();
                                    })
                                    | new Button("Decline").Icon(Icons.X).Outline().OnClick(() =>
                                    {
                                        planService.UpdateRecommendationState(selectedPlan.FolderName, recTitle, RecommendationStatus.Declined);
                                        refreshPlans();
                                        planContentQuery.Mutator.Revalidate();
                                    });

                    recommendationsLayout |= card;
                    recommendationsLayout |= buttonRow;
                    recommendationsLayout |= new Separator();
                }

            var changesTabView = new ChangesTabView(planData.AllChanges, planContentQuery.Loading, planContentQuery.Error);

            var tabNamesList = new List<string> { "summary", "plan", "details", "verifications", "git", "changes" };
            var tabList = new List<Tab>
            {
                new Tab("Summary", Cap(new SummaryTabView(planData.SummaryMarkdown, planContentQuery.Loading))),
                new Tab("Plan", Cap(planTabContent)),
                new Tab("Details", Cap(new DetailsTabView(selectedPlan,
                    jobService.GetJobsForPlan(selectedPlan.FolderName),
                    showDebugJob, planService, selectedPlanState, refreshPlans))),
                new Tab("Verifications", Cap(new VerificationsTabView(
                    selectedPlan.Verifications, planData.VerificationReports,
                    v => openVerification.Set(v)))).Badge(selectedPlan.Verifications.Count.ToString()),
                new Tab("Git", Cap(gitTabView)).Badge((gitData.WorktreeSections.Count + selectedPlan.Commits.Count + selectedPlan.Prs.Count).ToString()),
                new Tab("Changes", Layout.Vertical().Width(Size.Full()).Height(Size.Full().Min(Size.Px(0))) | changesTabView).Badge(changesTabView.FileCount > 0 ? changesTabView.FileCount.ToString() : "")
            };

            if (totalArtifacts > 0)
            {
                tabList.Add(new Tab("Artifacts", Cap(new ArtifactsTabView(planData.Artifacts))).Badge(totalArtifacts.ToString()));
                tabNamesList.Add("Artifacts");
            }

            if (pendingRecs.Count > 0)
            {
                tabList.Add(new Tab("Recommendations", Cap(recommendationsLayout)).Badge(pendingRecs.Count.ToString()));
                tabNamesList.Add("recommendations");
            }

            var actualTabNames = tabNamesList.ToArray();
            var actualSelectedTabIndex = Array.IndexOf(actualTabNames, args?.Tab ?? "summary");
            if (actualSelectedTabIndex < 0) actualSelectedTabIndex = 0;

            var tabs = Layout.Tabs(tabList.ToArray()).OnSelect(v =>
            {
                if (v >= 0 && v < actualTabNames.Length && selectedPlanState.Value != null)
                    nav.Navigate<ReviewApp>(new ReviewAppArgs(selectedPlanState.Value.FolderName, actualTabNames[v]));
            }).SelectedIndex(actualSelectedTabIndex).Variant(TabsVariant.Content).RemoveParentPadding();

            content |= (Layout.Vertical().Padding(2).Gap(0).Height(Size.Grow().Min(Size.Px(0))) | tabs);
        }

        content |= new VerificationReportSheet(openVerification, selectedPlan);
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
            ).Width(Size.Half()).Resizable();
        }

        content |= new FileSheet(openFile, config);

        return content;

        object Cap(object inner)
        {
            return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical().Padding(0, 0, 0, 4).Width(Size.Full().Max(Size.Units(200))) | inner);
        }
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
                var psi = new ProcessStartInfo("git", "fetch origin")
                {
                    WorkingDirectory = worktreePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
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
                var refreshed = planService.GetPlanByFolder(planFolder);
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
}
