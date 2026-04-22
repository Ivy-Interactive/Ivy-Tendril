using Ivy.Core;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Views.Sheets;
using Ivy.Tendril.Views.Tabs;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Review;

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
    private readonly List<PlanFile> _allPlans = allPlans;
    private readonly IConfigService _config = config;
    private readonly IGitService _gitService = gitService;
    private readonly IJobService _jobService = jobService;
    private readonly IPlanReaderService _planService = planService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile? _selectedPlan = selectedPlan;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var logger = UseService<ILogger<ContentView>>();
        var copyToClipboard = UseClipboard();
        var openVerification = UseState<string?>(null);
        var openArtifact = UseState<string?>(null);
        var openFile = UseState<string?>(null);
        var openCommit = UseState<string?>(null);
        var discardDialogOpen = UseState(false);
        var suggestChangesOpen = UseState(false);
        var suggestChangesText = UseState("");
        var customPrOpen = UseState(false);
        var rerunDialogOpen = UseState(false);

        var githubService = UseService<IGithubService>();
        var assigneesError = UseState<string?>(null);
        var assigneesQuery = UseQuery<string[], string>(
            _selectedPlan?.Project ?? "",
            async (_, _) =>
            {
                if (_selectedPlan is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repos = _selectedPlan.GetEffectiveRepoPaths(_config);
                var repoPath = repos.FirstOrDefault();
                if (repoPath is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repoConfig = GithubService.GetRepoConfigFromPath(repoPath);
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
        var selectedTab = UseState(0);

        var artifactContentQuery = UseQuery<string, string>(
            openArtifact.Value ?? "",
            async (filePath, ct) =>
            {
                if (string.IsNullOrEmpty(filePath)) return "";
                if (_selectedPlan is null) return "";
                var artifactsDir = Path.GetFullPath(Path.Combine(_selectedPlan.FolderPath, "artifacts"));
                var resolvedPath = Path.GetFullPath(filePath);
                if (!resolvedPath.StartsWith(artifactsDir, StringComparison.OrdinalIgnoreCase))
                    return "Access denied: file is outside the artifacts folder.";
                return await Task.Run(() =>
                    File.Exists(resolvedPath) ? FileHelper.ReadAllText(resolvedPath) : "File not found.", ct);
            },
            initialValue: ""
        );

        var planContentQuery = UseQuery<PlanContentData, string>(
            _selectedPlan?.FolderPath ?? "",
            async (folderPath, ct) =>
            {
                return await Task.Run(() =>
                {
                    if (_selectedPlan is null)
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
                    var summPath = Path.Combine(folderPath, "artifacts", "summary.md");
                    var summaryMd = File.Exists(summPath) ? FileHelper.ReadAllText(summPath) : null;

                    // Artifacts
                    var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

                    // Commit rows
                    var commitRows = PlanContentHelpers.BuildCommitRows(_selectedPlan!, _config, _gitService);

                    // All changes data
                    var allChanges = PlanContentHelpers.GetAllChangesData(_selectedPlan!, _config, _gitService);

                    // Verification report existence
                    var verReports = _selectedPlan.Verifications.ToDictionary(
                        v => v.Name,
                        v => File.Exists(Path.Combine(folderPath, "verification", $"{v.Name}.md")));

                    // Review action conditions
                    var projectConfig = _config.GetProject(_selectedPlan.Project);
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
                        actionStates[i] = (action.Name, PlatformHelper.EvaluatePowerShellCondition(action.Condition, folderPath));
                    });

                    return new PlanContentData(recs, summaryMd, artifacts, commitRows, verReports, actionStates.ToList(), allChanges);
                }, ct);
            },
            options: QueryScope.View,
            initialValue: new PlanContentData(new List<RecommendationYaml>(), null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(),
                new List<(string Name, bool ConditionMet)>(), null)
        );

        UseEffect(() => { selectedTab.Set(0); }, _selectedPlanState);

        if (_selectedPlan is null)
        {
            if (_allPlans.Count == 0)
                return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                       | new Icon(Icons.Inbox).Large().Color(Colors.Gray)
                       | Text.Muted("No plans to review");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a completed plan to review");
        }

        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan.FolderName);

        // Header
        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{_selectedPlan.Id} {_selectedPlan.Title}").Bold().NoWrap().Overflow(Overflow.Ellipsis);

        if (!string.IsNullOrEmpty(_selectedPlan.SourceUrl))
            header |= new Button(_selectedPlan.SourceUrl.Contains("/pull/") ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => client.OpenUrl(_selectedPlan.SourceUrl));

        header |= new Spacer().Width(Size.Grow());

        header |= Text.Rich()
                         .Bold($"{currentIndex + 1}/{_allPlans.Count}", word: true)
                         .Muted("plans", word: true);

        header |= new Button("Create PR").Icon(Icons.GitPullRequest).Primary().OnClick(() =>
        {
            var repoPaths = _selectedPlan.GetEffectiveRepoPaths(_config);
            var project = _config.GetProject(_selectedPlan.Project);
            var allYolo = repoPaths.All(rp =>
                project?.GetRepoRef(rp)?.PrRule == "yolo");

            if (allYolo)
            {
                // Optimistically update UI state before disk I/O
                var optimisticPlan = _selectedPlan with
                {
                    Metadata = _selectedPlan.Metadata with { State = PlanStatus.Building }
                };
                _selectedPlanState.Set(optimisticPlan);

                _jobService.StartJob("CreatePr", _selectedPlan.FolderPath);
                _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Building);
                _refreshPlans();
            }
            else
            {
                customPrOpen.Set(true);
            }
        }).ShortcutKey("m").WithConfetti(AnimationTrigger.Click);

        // Content sections
        var content = Layout.Vertical();

        var planData = planContentQuery.Value;

        // Early null guard: _selectedPlan may become null due to state updates
        if (_selectedPlan is null)
        {
            return Text.Muted("No plan selected");
        }

        // Plan tab content (not dependent on query — uses in-memory data)
        var reviewAnnotated = MarkdownHelper.AnnotateAllBrokenLinks(_selectedPlan.LatestRevisionContent, _planService.PlansDirectory);
        var planTabContent = new Markdown(reviewAnnotated)
            .DangerouslyAllowLocalFiles()
            .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile, planId =>
            {
                var planFolder = Directory.GetDirectories(_planService.PlansDirectory, $"{planId:D5}-*")
                    .FirstOrDefault();
                if (planFolder != null)
                {
                    var plan = _planService.GetPlanByFolder(planFolder);
                    if (plan != null)
                        _selectedPlanState.Set(plan);
                }
            }));

        if (planContentQuery.Loading)
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
            // Git tab content (uses shared helper)
            var gitData = GitTabHelper.BuildGitTabData(_selectedPlan!, _config, _gitService);
            var gitLayout = GitTabHelper.RenderGitTab(
                gitData,
                _selectedPlan!,
                client,
                _config,
                hash => openCommit.Set(hash),
                path =>
                {
                    copyToClipboard(path);
                    client.Toast("Copied path to clipboard", "Path Copied");
                    return null!; // Return type doesn't matter, just need to satisfy Func
                }
            );

            // Artifacts tab content
            var totalArtifacts = (planData.Artifacts.GetValueOrDefault("screenshots")?.Count ?? 0)
                                 + (planData.Artifacts.ContainsKey("sample") ? 1 : 0);

            // Review actions
            var reviewActionStates = planData.ReviewActionStates;
            var projectConfig = _config.GetProject(_selectedPlan.Project);
            var reviewActions = projectConfig?.ReviewActions ?? new List<ReviewActionConfig>();
            if (reviewActions.Count > 0)
            {
                var actionsBar = Layout.Horizontal().Gap(2).Padding(1);
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
                            if (!PlatformHelper.RunPowerShellAction(actionCapture.Action, _selectedPlan.FolderPath))
                            {
                                Console.Error.WriteLine($"Failed to run review action '{actionCapture.Name}': pwsh not found");
                            }
                        });
                    }

                    actionsBar |= btn;
                }

                content |= actionsBar;
            }

            // Recommendations tab content
            var recommendationsLayout = Layout.Vertical().Gap(4).Padding(2);
            if (planData.Recommendations.Count == 0)
                recommendationsLayout |= Text.Muted("No recommendations.");
            else
                foreach (var rec in planData.Recommendations)
                {
                    var titleRow = Layout.Horizontal().Gap(2).AlignContent(Align.Center)
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
                               | new Markdown(rec.Description).DangerouslyAllowLocalFiles();
                    recommendationsLayout |= card;
                    recommendationsLayout |= new Separator();
                }

            // Changes tab
            var changesTabView = new ChangesTabView(planData.AllChanges, planContentQuery.Loading, planContentQuery.Error);

            // Build tabs
            var tabs = Layout.Tabs(
                new Tab("Summary", Cap(new SummaryTabView(planData.SummaryMarkdown))),
                new Tab("Verifications", Cap(new VerificationsTabView(
                    _selectedPlan.Verifications, planData.VerificationReports,
                    v => openVerification.Set(v)))).Badge(_selectedPlan.Verifications.Count.ToString()),
                new Tab("Git", Cap(gitLayout)).Badge((_selectedPlan.Commits.Count + _selectedPlan.Prs.Count).ToString()),
                new Tab("Changes", Cap(changesTabView)).Badge(changesTabView.FileCount > 0 ? changesTabView.FileCount.ToString() : ""),
                new Tab("Artifacts", Cap(new ArtifactsTabView(planData.Artifacts))).Badge(totalArtifacts.ToString()),
                new Tab("Recommendations", Cap(recommendationsLayout)).Badge(planData.Recommendations.Count.ToString()),
                new Tab("Plan", Cap(planTabContent))
            ).OnSelect(v => selectedTab.Set(v)).SelectedIndex(selectedTab.Value).Variant(TabsVariant.Content);

            content |= tabs;
        }

        // Sheet modals (outside TabsLayout so they render as overlays)
        content |= new VerificationReportSheet(openVerification, _selectedPlan);
        content |= new CommitDetailSheet(openCommit, _selectedPlan, _config, _gitService);

        if (openArtifact.Value is { } artifactPath)
        {
            var language = FileApp.GetLanguage(Path.GetExtension(artifactPath));
            content |= new Sheet(
                () => openArtifact.Set(null),
                artifactContentQuery.Loading
                    ? Text.Muted("Loading...")
                    : artifactContentQuery.Error is { } err
                        ? Text.Muted($"Failed to load artifact: {err.Message}")
                        : new Markdown($"```{language.ToString().ToLowerInvariant()}\n{artifactContentQuery.Value}\n```"),
                Path.GetFileName(artifactPath)
            ).Width(Size.Half()).Resizable();
        }

        if (_selectedPlan is not null)
        {
            var fileRepoPaths = _selectedPlan.GetEffectiveRepoPaths(_config);
            var fileLinkSheet =
                FileLinkHelper.BuildFileLinkSheet(openFile.Value, () => openFile.Set(null), fileRepoPaths, _config);
            if (fileLinkSheet != null) content |= fileLinkSheet;
        }

        // Dialogs
        content |= new SuggestChangesDialog(suggestChangesOpen, suggestChangesText, _selectedPlan, _jobService,
            _planService, _refreshPlans);
        content |= new CustomPrDialog(customPrOpen, _selectedPlan, _jobService, _planService, _refreshPlans,
            assigneesQuery, assigneesError);

        // Discard confirmation dialog
        content |= new DiscardPlanDialog(discardDialogOpen, _selectedPlan, _planService, _refreshPlans);

        // Rerun dialog
        content |= new RerunDialog(rerunDialogOpen, _selectedPlan, _jobService, _planService, _refreshPlans);

        // Action bar
        var actionBar = Layout.Horizontal().AlignContent(Align.Center).Gap(2).Padding(1)
                        | new Button("Rerun").Icon(Icons.RotateCw).Outline().ShortcutKey("r").OnClick(() =>
                        {
                            rerunDialogOpen.Set(true);
                        })
                        | new Button("Suggest Changes").Icon(Icons.MessageSquare).Outline().OnClick(() =>
                        {
                            suggestChangesOpen.Set(true);
                        }).ShortcutKey("d")
                        | new Button("Discard").Icon(Icons.Trash).Outline().ShortcutKey("Backspace").OnClick(() =>
                        {
                            discardDialogOpen.Set(true);
                        })
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious())
                            .ShortcutKey("p")
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext())
                            .ShortcutKey("n")
                        | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                            new MenuItem("Custom PR", Icon: Icons.GitPullRequest, Tag: "CustomPR").OnSelect(() =>
                            {
                                customPrOpen.Set(true);
                            }),
                            new MenuItem("Set Completed", Icon: Icons.CircleCheck, Tag: "SetCompleted").OnSelect(() =>
                            {
                                // Optimistically update UI state before disk I/O
                                var optimisticPlan = _selectedPlan with
                                {
                                    Metadata = _selectedPlan.Metadata with { State = PlanStatus.Completed }
                                };
                                _selectedPlanState.Set(optimisticPlan);

                                _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Completed);
                                _refreshPlans();
                            }),
                            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                                .OnSelect(() => { PlatformHelper.OpenInFileManager(_selectedPlan.FolderPath); }),
                            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
                            {
                                PlatformHelper.OpenInTerminal(_selectedPlan.FolderPath);
                            }),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    copyToClipboard(_selectedPlan.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem($"Open in {_config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                                .OnSelect(() => { _config.OpenInEditor(_selectedPlan.FolderPath); }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var yamlPath = Path.Combine(_selectedPlan.FolderPath, "plan.yaml");
                                _config.OpenInEditor(yamlPath);
                            })
                        );

        return new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(_selectedPlan.Id);

        object Cap(object inner)
        {
            return Layout.Vertical().Width(Size.Auto().Max(Size.Units(200))) | inner;
        }
    }

    internal static bool ValidateArtifactPath(string filePath, string planFolderPath)
    {
        var artifactsDir = Path.GetFullPath(Path.Combine(planFolderPath, "artifacts"));
        var resolvedPath = Path.GetFullPath(filePath);
        return resolvedPath.StartsWith(artifactsDir, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ValidateVerificationPath(string name, string planFolderPath)
    {
        var verificationDir = Path.GetFullPath(Path.Combine(planFolderPath, "verification"));
        var resolvedPath = Path.GetFullPath(Path.Combine(verificationDir, $"{name}.md"));
        return resolvedPath.StartsWith(verificationDir, StringComparison.OrdinalIgnoreCase);
    }

    private void GoToNext()
    {
        if (_allPlans.Count == 0) return;
        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan?.FolderName);
        var nextIndex = (currentIndex + 1) % _allPlans.Count;
        _selectedPlanState.Set(_allPlans[nextIndex]);
    }

    private void GoToPrevious()
    {
        if (_allPlans.Count == 0) return;
        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan?.FolderName);
        var prevIndex = (currentIndex - 1 + _allPlans.Count) % _allPlans.Count;
        _selectedPlanState.Set(_allPlans[prevIndex]);
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
