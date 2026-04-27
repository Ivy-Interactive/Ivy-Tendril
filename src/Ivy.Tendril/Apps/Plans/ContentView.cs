using Ivy.Tendril.Models;
using System.Text.RegularExpressions;
using Ivy.Core;
using Ivy.Tendril.Apps.Plans.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Views;
using Ivy.Tendril.Views.Sheets;
using Ivy.Tendril.Views.Tabs;

namespace Ivy.Tendril.Apps.Plans;

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
        var downloadUrl = PlanDownloadHelper.UsePlanDownload(Context, _planService, _selectedPlan);
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var updateDialogOpen = UseState(false);
        var deleteDialogOpen = UseState(false);
        var createIssueDialogOpen = UseState(false);
        var openFile = UseState<string?>(null);
        var selectedRepoState = UseState<string?>(null);
        var issueAssigneeState = UseState<string?>(null);
        var issueLabelsState = UseState<string[]>([]);
        var issueCommentState = UseState("");

        var updateText = UseState("");
        var isEditing = UseState(false);
        var editContent = UseState("");
        var originalContent = UseState("");
        var isEditingPrev = UseState(false);
        var lastPlanId = UseState(_selectedPlan?.Id ?? -1);

        var selectedTab = UseState(0);
        var openVerification = UseState<string?>(null);
        var openCommit = UseState<string?>(null);

        var selectedPlanRef = UseRef(_selectedPlan);

        var verificationReportQuery = UseQuery<string, string>(
            openVerification.Value ?? "",
            async (name, ct) =>
            {
                if (string.IsNullOrEmpty(name) || _selectedPlan is null) return "";
                var verificationDir = Path.GetFullPath(Path.Combine(_selectedPlan.FolderPath, "verification"));
                var resolvedPath = Path.GetFullPath(Path.Combine(verificationDir, $"{name}.md"));
                if (!resolvedPath.StartsWith(verificationDir, StringComparison.OrdinalIgnoreCase))
                    return "Access denied: file is outside the verification folder.";
                return await Task.Run(() =>
                    File.Exists(resolvedPath) ? FileHelper.ReadAllText(resolvedPath) : $"No report found for {name}.", ct);
            },
            initialValue: ""
        );

        var commitQuery = UseQuery<PlanContentHelpers.CommitDetailData?, string>(
            openCommit.Value ?? "",
            async (hash, ct) =>
            {
                if (string.IsNullOrEmpty(hash)) return null;
                var repoPaths2 = _selectedPlan!.GetEffectiveRepoPaths(_config);
                return await Task.Run(() =>
                {
                    foreach (var repo in repoPaths2)
                    {
                        var titleResult = _gitService.GetCommitTitle(repo, hash);
                        if (titleResult.IsSuccess)
                        {
                            var diff = _gitService.GetCommitDiff(repo, hash).GetValueOrDefault();
                            var files = _gitService.GetCommitFiles(repo, hash).GetValueOrDefault();
                            return new PlanContentHelpers.CommitDetailData(titleResult.Value!, diff, files);
                        }
                    }
                    return null;
                }, ct);
            },
            initialValue: null
        );

        var planContentQuery = UseQuery<PlanContentData, string>(
            _selectedPlan?.FolderPath ?? "",
            async (folderPath, ct) =>
            {
                return await Task.Run(() =>
                {
                    if (_selectedPlan is null)
                        return new PlanContentData(null,
                            new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(),
                            new Dictionary<string, bool>());

                    // Summary
                    var summPath = Path.Combine(folderPath, "artifacts", "summary.md");
                    var summaryMd = File.Exists(summPath) ? FileHelper.ReadAllText(summPath) : null;

                    // Artifacts
                    var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

                    // Commit rows
                    var commitRows = PlanContentHelpers.BuildCommitRows(_selectedPlan!, _config, _gitService);

                    // Verification report existence
                    var verReports = _selectedPlan.Verifications.ToDictionary(
                        v => v.Name,
                        v => File.Exists(Path.Combine(folderPath, "verification", $"{v.Name}.md")));

                    return new PlanContentData(summaryMd, artifacts, commitRows, verReports);
                }, ct);
            },
            initialValue: new PlanContentData(null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>())
        );

        UseEffect(() =>
        {
            var plan = selectedPlanRef.Value;
            if (isEditing.Value && !isEditingPrev.Value)
            {
                if (plan != null)
                {
                    var raw = _planService.ReadRawPlan(plan.FolderName);
                    editContent.Set(raw);
                    originalContent.Set(raw);
                }
                else
                {
                    isEditing.Set(false);
                }
            }
            else if (!isEditing.Value && isEditingPrev.Value)
            {
                if (plan != null && editContent.Value != originalContent.Value)
                {
                    _planService.SaveRevision(plan.FolderName, editContent.Value);
                    _refreshPlans();
                }
            }

            isEditingPrev.Set(isEditing.Value);
        }, isEditing);

        UseEffect(() => { selectedTab.Set(0); }, _selectedPlanState);

#pragma warning disable CS8601
        selectedPlanRef.Value = _selectedPlan;
#pragma warning restore CS8601

        if (lastPlanId.Value != (_selectedPlan?.Id ?? -1))
        {
            lastPlanId.Set(_selectedPlan?.Id ?? -1);
            isEditing.Set(false);
        }

        if (_selectedPlan is null)
        {
            if (_allPlans.Count == 0)
                return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(4).Padding(8)
                       | new Icon(Icons.Feather).Large().Color(Colors.Gray)
                       | Text.H3("No draft plans")
                       | new NewPlanButton().Width(Size.Fit());

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");
        }

        var currentIndex = _allPlans.FindIndex(p => p.FolderName == _selectedPlan.FolderName);
        var planData = planContentQuery.Value;

        var header = BuildPlanHeader(currentIndex, isEditing, client);
        var planTabContent = BuildPlanTabContent(isEditing, editContent, openFile);
        var content = BuildContentLayout(planContentQuery, planData, planTabContent, selectedTab, openVerification, openCommit, verificationReportQuery, commitQuery, client);
        var actionBar = BuildActionBar(updateDialogOpen, deleteDialogOpen, createIssueDialogOpen, downloadUrl, client, copyToClipboard);

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(_selectedPlan.Id);

        var elements = new List<object>
        {
            mainLayout,
            new UpdatePlanDialog(updateDialogOpen, updateText, _selectedPlan, _selectedPlanState, _jobService, _planService, _refreshPlans),
            new DeletePlanDialog(deleteDialogOpen, _selectedPlan, _selectedPlanState, _planService, _refreshPlans),
            new CreateIssueDialog(createIssueDialogOpen, selectedRepoState, issueAssigneeState, issueLabelsState,
                issueCommentState, _selectedPlan, _jobService)
        };

        var repoPaths = _selectedPlan.GetEffectiveRepoPaths(_config);
        var fileLinkSheet = FileLinkHelper.BuildFileLinkSheet(
            openFile.Value, () => openFile.Set(null), repoPaths, _config);
        if (fileLinkSheet is not null)
            elements.Add(fileLinkSheet);

        return new Fragment(elements.ToArray());
    }

    private object BuildPlanHeader(int currentIndex, IState<bool> isEditing, IClientProvider client)
    {
        var header = Layout.Horizontal().Width(Size.Full()).Padding(1).Gap(2)
                     | Text.Block($"#{_selectedPlan!.Id} {_selectedPlan.Title}").Bold();
        header |= Text.Muted($"rev:{_selectedPlan.RevisionCount}");

        if (!string.IsNullOrEmpty(_selectedPlan.SourceUrl))
            header |= new Button(_selectedPlan.SourceUrl.Contains("/pull/") ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => client.OpenUrl(_selectedPlan.SourceUrl));

        if (_selectedPlan.DependsOn.Count > 0)
        {
            var depIds = string.Join(", ", _selectedPlan.DependsOn.Select(d =>
            {
                var name = Path.GetFileName(d);
                var dashIdx = name.IndexOf('-');
                return dashIdx > 0 ? name[..dashIdx] : name;
            }));
            header |= new Badge($"Depends on: {depIds}").Variant(BadgeVariant.Secondary);
        }

        header |= isEditing.ToSwitchInput(Icons.Code);
        header |= new Spacer().Width(Size.Grow());
        header |= Text.Rich()
            .Bold($"{currentIndex + 1}/{_allPlans.Count}", word: true)
            .Muted("plans", word: true);
        header |= new Button("Execute").Icon(Icons.Rocket).Primary().ShortcutKey("e").OnClick(() =>
        {
            var optimisticPlan = _selectedPlan with
            {
                Metadata = _selectedPlan.Metadata with { State = PlanStatus.Building }
            };
            _selectedPlanState.Set(optimisticPlan);

            _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Building);
            _jobService.StartJob("ExecutePlan", _selectedPlan.FolderPath);
            _refreshPlans();
        });

        return header;
    }

    private object BuildPlanTabContent(IState<bool> isEditing, IState<string> editContent, IState<string?> openFile)
    {
        if (isEditing.Value)
            return editContent.ToCodeInput()
                .Language(Languages.Markdown)
                .Width(Size.Full());

        var planLayout = Layout.Vertical();
        if (_selectedPlan!.Status == PlanStatus.Failed)
            planLayout |= BuildFailureCallout(_selectedPlan);

        var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(_selectedPlan.LatestRevisionContent, _planService.PlansDirectory);
        planLayout |= new Markdown(annotatedContent)
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

        return planLayout;
    }

    private object BuildContentLayout(
        QueryResult<PlanContentData> planContentQuery,
        PlanContentData planData,
        object planTabContent,
        IState<int> selectedTab,
        IState<string?> openVerification,
        IState<string?> openCommit,
        QueryResult<string> verificationReportQuery,
        QueryResult<PlanContentHelpers.CommitDetailData?> commitQuery,
        IClientProvider client)
    {
        var content = Layout.Vertical();

        if (planContentQuery.Loading)
        {
            content |= Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                       | Text.Muted("Loading...");
            return content;
        }

        var summaryTabContent = BuildSummaryTabContent(planData);
        var verificationsTable = BuildVerificationsTable(planData, openVerification);
        var commitsContent = BuildCommitsContent(planData, openCommit);
        var prsContent = BuildPrsContent(client);
        var artifactsContent = BuildArtifactsContent(planData, out var totalArtifacts);

        var tabs = Layout.Tabs(
            new Tab("Plan", Cap(planTabContent)),
            new Tab("Summary", Cap(summaryTabContent)),
            new Tab("Verifications", Cap(verificationsTable)).Badge(_selectedPlan!.Verifications.Count.ToString()),
            new Tab("Commits", Cap(commitsContent)).Badge(_selectedPlan.Commits.Count.ToString()),
            new Tab("PRs", Cap(prsContent)).Badge(_selectedPlan.Prs.Count.ToString()),
            new Tab("Artifacts", Cap(artifactsContent)).Badge(totalArtifacts.ToString())
        ).OnSelect(v => selectedTab.Set(v)).SelectedIndex(selectedTab.Value).Variant(TabsVariant.Content);

        content |= tabs;

        // Sheet modals
        if (openVerification.Value is { } verName)
            content |= new Sheet(
                () => openVerification.Set(null),
                verificationReportQuery.Loading
                    ? Text.Muted("Loading...")
                    : new Markdown(verificationReportQuery.Value).DangerouslyAllowLocalFiles(),
                verName
            ).Width(Size.Half()).Resizable();

        if (openCommit.Value is { } commitHash && _selectedPlan is not null)
        {
            content |= PlanContentHelpers.RenderCommitDetailSheet(
                commitQuery.Value,
                commitQuery.Loading || commitQuery.Value is null && !string.IsNullOrEmpty(openCommit.Value),
                commitHash,
                () => openCommit.Set(null));
        }

        return content;

        object Cap(object inner) => Layout.Vertical().Width(Size.Auto().Max(Size.Units(200))) | inner;
    }

    private object BuildSummaryTabContent(PlanContentData planData)
    {
        if (planData.SummaryMarkdown is { } summaryMd)
            return new Markdown(summaryMd).DangerouslyAllowLocalFiles();
        return Text.Muted("No summary available.");
    }

    private object BuildVerificationsTable(PlanContentData planData, IState<string?> openVerification)
    {
        var verificationsTable = new Table(
            new TableRow(
                    new TableCell("Status").IsHeader(),
                    new TableCell("Name").IsHeader()
                )
            { IsHeader = true }
        );

        foreach (var v in _selectedPlan!.Verifications)
        {
            var hasReport = planData.VerificationReports.TryGetValue(v.Name, out var exists) && exists;
            var nameCapture = v.Name;
            var nameCell = hasReport
                ? new Button(v.Name).Inline().OnClick(() => openVerification.Set(nameCapture))
                : (object)Text.Block(v.Name);

            verificationsTable |= new TableRow(
                new TableCell(new Badge(v.Status).Variant(
                    StatusMappings.VerificationStatusBadgeVariants.TryGetValue(v.Status, out var variant)
                        ? variant
                        : BadgeVariant.Outline)),
                new TableCell(nameCell)
            );
        }

        return verificationsTable;
    }

    private object BuildCommitsContent(PlanContentData planData, IState<string?> openCommit)
    {
        var commitsTable = new Table(
            new TableRow(
                    new TableCell("Commit").IsHeader(),
                    new TableCell("Message").IsHeader(),
                    new TableCell("Files").IsHeader()
                )
            { IsHeader = true }
        );

        foreach (var row in planData.CommitRows)
            commitsTable |= new TableRow(
                new TableCell(new Button(row.ShortHash).Inline().OnClick(() => openCommit.Set(row.Hash))),
                new TableCell(row.Title),
                new TableCell(row.FileCount?.ToString() ?? "–")
            );

        var commitWarning = PlanContentHelpers.BuildCommitWarningCallout(planData.CommitRows);
        return commitWarning != null
            ? Layout.Vertical().Gap(2) | commitWarning | commitsTable
            : commitsTable;
    }

    private object BuildPrsContent(IClientProvider client)
    {
        if (_selectedPlan!.Prs.Count == 0)
            return new Empty();

        var prsTable = new Table(
            new TableRow(
                    new TableCell("Repository").IsHeader(),
                    new TableCell("PR").IsHeader()
                )
            { IsHeader = true }
        );

        foreach (var pr in _selectedPlan.Prs.Where(PullRequestApp.IsValidUrl))
        {
            var prCapture = pr;
            prsTable |= new TableRow(
                new TableCell(PullRequestApp.ExtractRepo(pr)),
                new TableCell(new Button(pr).Link().OnClick(() => client.OpenUrl(prCapture)))
            );
        }

        return prsTable;
    }

    private object BuildArtifactsContent(PlanContentData planData, out int totalArtifacts)
    {
        var artifactsLayout = Layout.Vertical().Gap(2);
        artifactsLayout |= PlanContentHelpers.RenderArtifactScreenshots(planData.Artifacts);

        totalArtifacts = (planData.Artifacts.GetValueOrDefault("screenshots")?.Count ?? 0)
                         + (planData.Artifacts.ContainsKey("sample") ? 1 : 0);

        return artifactsLayout;
    }

    private object BuildActionBar(
        IState<bool> updateDialogOpen,
        IState<bool> deleteDialogOpen,
        IState<bool> createIssueDialogOpen,
        IState<string?> downloadUrl,
        IClientProvider client,
        Action<string> copyToClipboard)
    {
        var actionBar = Layout.Horizontal().AlignContent(Align.Center).Gap(2).Padding(1)
                        | new Button("Update").Icon(Icons.Pencil).Outline().ShortcutKey("u")
                            .OnClick(() => updateDialogOpen.Set(true))
                        | new Button("Split").Icon(Icons.Scissors).Outline().ShortcutKey("s").OnClick(() =>
                        {
                            var optimisticPlan = _selectedPlan! with
                            {
                                Metadata = _selectedPlan.Metadata with { State = PlanStatus.Updating }
                            };
                            _selectedPlanState.Set(optimisticPlan);

                            _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Updating);
                            _jobService.StartJob("SplitPlan", _selectedPlan.FolderPath);
                            _refreshPlans();
                        })
                        | new Button("Expand").Icon(Icons.UnfoldVertical).Outline().ShortcutKey("x").OnClick(() =>
                        {
                            var optimisticPlan = _selectedPlan! with
                            {
                                Metadata = _selectedPlan.Metadata with { State = PlanStatus.Building }
                            };
                            _selectedPlanState.Set(optimisticPlan);

                            _planService.TransitionState(_selectedPlan.FolderName, PlanStatus.Building);
                            _jobService.StartJob("ExpandPlan", _selectedPlan.FolderPath);
                            _refreshPlans();
                        })
                        | new Button("Delete").Icon(Icons.Trash).Outline().ShortcutKey("Backspace")
                            .OnClick(() => deleteDialogOpen.Set(true))
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious())
                            .ShortcutKey("p")
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext())
                            .ShortcutKey("n")
                        | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                            new MenuItem("Create Issue", Icon: Icons.Github, Tag: "CreateIssue").OnSelect(() =>
                                createIssueDialogOpen.Set(true)),
                            new MenuItem("Download", Icon: Icons.Download, Tag: "Download").OnSelect(() =>
                            {
                                var url = downloadUrl.Value;
                                if (!string.IsNullOrEmpty(url)) client.OpenUrl(url);
                            }),
                            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                                .OnSelect(() => { PlatformHelper.OpenInFileManager(_selectedPlan!.FolderPath); }),
                            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
                            {
                                PlatformHelper.OpenInTerminal(_selectedPlan!.FolderPath);
                            }),
                            new MenuItem($"Open in {_config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                                .OnSelect(() => { _config.OpenInEditor(_selectedPlan!.FolderPath); }),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    copyToClipboard(_selectedPlan!.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem("Copy Plan to Clipboard", Icon: Icons.Share, Tag: "CopyPlan")
                                .OnSelect(() =>
                                {
                                    var exported = PlanExportHelper.ExportToClipboard(_selectedPlan!);
                                    copyToClipboard(exported);
                                    client.Toast("Plan copied to clipboard", "Plan Exported");
                                }),
                            new MenuItem("Mark as Completed", Icon: Icons.CircleCheck, Tag: "MarkCompleted")
                                .OnSelect(() =>
                                {
                                    _planService.TransitionState(_selectedPlan!.FolderName, PlanStatus.Completed);
                                    _refreshPlans();
                                }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var yamlPath = Path.Combine(_selectedPlan!.FolderPath, "plan.yaml");
                                _config.OpenInEditor(yamlPath);
                            })
                        );

        return actionBar;
    }

    internal static object BuildFailureCallout(PlanFile plan)
    {
        var verificationFailure = TryBuildVerificationFailureMessage(plan);
        if (verificationFailure != null)
            return verificationFailure;

        var logFailure = TryBuildLogFailureMessage(plan);
        if (logFailure != null)
            return logFailure;

        return Callout.Destructive("No details available. Check the logs folder.", "Execution Failed");
    }

    private static object? TryBuildVerificationFailureMessage(PlanFile plan)
    {
        var verificationDir = Path.Combine(plan.FolderPath, "verification");
        if (!Directory.Exists(verificationDir))
            return null;

        var failedVerifications = plan.Verifications
            .Where(v => v.Status is "Fail" or "Pending")
            .ToList();

        if (failedVerifications.Count == 0)
            return null;

        var parts = failedVerifications.Select(v => FormatVerificationFailure(verificationDir, v)).ToList();
        return Callout.Destructive(string.Join("\n\n", parts), "Execution Failed");
    }

    private static string FormatVerificationFailure(string verificationDir, PlanVerificationEntry v)
    {
        var reportPath = Path.Combine(verificationDir, $"{v.Name}.md");
        if (!File.Exists(reportPath))
            return $"**{v.Name}** — {v.Status}, no report generated";

        var report = FileHelper.ReadAllText(reportPath);
        var detail = ExtractVerificationDetail(report);
        return $"**{v.Name}** — {detail}";
    }

    private static string ExtractVerificationDetail(string report)
    {
        var outputMatch = Regex.Match(report, @"## Output\s*\n([\s\S]*?)(?=\n## |\z)");
        if (outputMatch.Success)
            return outputMatch.Groups[1].Value.Trim();

        var issuesMatch = Regex.Match(report, @"## Issues Found\s*\n([\s\S]*?)(?=\n## |\z)");
        if (issuesMatch.Success)
            return issuesMatch.Groups[1].Value.Trim();

        return "See verification report for details";
    }

    private static object? TryBuildLogFailureMessage(PlanFile plan)
    {
        var logsDir = Path.Combine(plan.FolderPath, "logs");
        if (!Directory.Exists(logsDir))
            return null;

        var lastLog = Directory.GetFiles(logsDir, "*.md")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        if (lastLog == null)
            return null;

        return FormatLogFailure(lastLog);
    }

    private static object FormatLogFailure(string logPath)
    {
        var logContent = FileHelper.ReadAllText(logPath);

        var summaryMatch = Regex.Match(logContent, @"## Summary\s*\n([\s\S]*?)(?=\n## |\z)");
        if (summaryMatch.Success)
            return Callout.Destructive(summaryMatch.Groups[1].Value.Trim(), "Execution Failed");

        var statusMatch = Regex.Match(logContent, @"\*\*Status:\*\*\s*(.+)");
        if (!statusMatch.Success)
            return Callout.Destructive("Log file exists but status cannot be parsed.", "Execution Failed");

        var status = statusMatch.Groups[1].Value.Trim();
        if (status == "Completed")
            return Callout.Warning(
                "Execution reported as completed but plan is in Failed state. The process may have crashed during state transition.",
                "State Mismatch");

        return Callout.Destructive($"Last execution status: {status}", "Execution Failed");
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
        string? SummaryMarkdown,
        Dictionary<string, List<string>> Artifacts,
        List<PlanContentHelpers.CommitRow> CommitRows,
        Dictionary<string, bool> VerificationReports);
}
