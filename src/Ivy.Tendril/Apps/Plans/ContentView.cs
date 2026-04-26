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
    public override object Build()
    {
        var downloadUrl = PlanDownloadHelper.UsePlanDownload(Context, planService, selectedPlan);
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
        var lastPlanId = UseState(selectedPlan?.Id ?? -1);

        var selectedTab = UseState(0);
        var openVerification = UseState<string?>(null);
        var openCommit = UseState<string?>(null);

        var selectedPlanRef = UseRef(selectedPlan);

        var planContentQuery = UseQuery<PlanContentData, string>(
            selectedPlan?.FolderPath ?? "",
            async (folderPath, ct) =>
            {
                return await Task.Run(() =>
                {
                    if (selectedPlan is null)
                        return new PlanContentData(null,
                            new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(),
                            new Dictionary<string, bool>(), null);

                    // Summary
                    var summPath = Path.Combine(folderPath, "artifacts", "summary.md");
                    var summaryMd = File.Exists(summPath) ? FileHelper.ReadAllText(summPath) : null;

                    // Artifacts
                    var artifacts = PlanContentHelpers.GetArtifacts(folderPath);

                    // Commit rows
                    var commitRows = PlanContentHelpers.BuildCommitRows(selectedPlan!, config, gitService);

                    // All changes data
                    var allChanges = PlanContentHelpers.GetAllChangesData(selectedPlan!, config, gitService);

                    // Verification report existence
                    var verReports = selectedPlan.Verifications.ToDictionary(
                        v => v.Name,
                        v => File.Exists(Path.Combine(folderPath, "verification", $"{v.Name}.md")));

                    return new PlanContentData(summaryMd, artifacts, commitRows, verReports, allChanges);
                }, ct);
            },
            initialValue: new PlanContentData(null,
                new Dictionary<string, List<string>>(), new List<PlanContentHelpers.CommitRow>(), new Dictionary<string, bool>(), null)
        );

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

        UseEffect(() => { selectedTab.Set(0); }, selectedPlanState);

#pragma warning disable CS8601
        selectedPlanRef.Value = selectedPlan;
#pragma warning restore CS8601

        if (lastPlanId.Value != (selectedPlan?.Id ?? -1))
        {
            lastPlanId.Set(selectedPlan?.Id ?? -1);
            isEditing.Set(false);
        }

        if (selectedPlan is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("No draft plans", "Plans you create will appear here.", new NewPlanButton().Width(Size.Fit()));

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");
        }

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var header = Layout.Horizontal().Width(Size.Full()).Height(Size.Px(40)).Gap(2)
                     | Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold();
        header |= Text.Muted($"rev:{selectedPlan.RevisionCount}");

        if (!string.IsNullOrEmpty(selectedPlan.SourceUrl))
            header |= new Button(selectedPlan.SourceUrl.Contains("/pull/") ? "PR" : "Issue")
                .Icon(Icons.ExternalLink).Ghost().OnClick(() => client.OpenUrl(selectedPlan.SourceUrl));

        if (selectedPlan.DependsOn.Count > 0)
        {
            var depIds = string.Join(", ", selectedPlan.DependsOn.Select(d =>
            {
                var name = Path.GetFileName(d);
                var dashIdx = name.IndexOf('-');
                return dashIdx > 0 ? name[..dashIdx] : name;
            }));
            header |= new Badge($"Depends on: {depIds}").Variant(BadgeVariant.Secondary);
        }

        header |= new Spacer().Width(Size.Grow());
        header |= Text.Rich()
            .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
            .Muted("plans", word: true);
        header |= new Button("Execute").Icon(Icons.Rocket).Primary().ShortcutKey("e").OnClick(() =>
        {
            // Optimistically update UI state before disk I/O
            var optimisticPlan = selectedPlan with
            {
                Metadata = selectedPlan.Metadata with { State = PlanStatus.Building }
            };
            selectedPlanState.Set(optimisticPlan);

            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
            jobService.StartJob("ExecutePlan", selectedPlan.FolderPath);
            refreshPlans();
        });

        // Build tab contents
        var content = Layout.Vertical().Height(Size.Full());
        var planData = planContentQuery.Value;

        // Plan tab content
        object planTabContent;
        if (isEditing.Value)
        {
            var editBar = Layout.Horizontal().Gap(2).Padding(0, 0, 2, 0)
                          | new Button("Save Revision").Icon(Icons.Save).Primary().OnClick(() =>
                          {
                              if (selectedPlan != null && editContent.Value != originalContent.Value)
                              {
                                  planService.SaveRevision(selectedPlan.FolderName, editContent.Value);
                                  var updated = planService.GetPlanByFolder(selectedPlan.FolderPath);
                                  if (updated != null) selectedPlanState.Set(updated);
                                  refreshPlans();
                              }
                              isEditing.Set(false);
                          })
                          | new Button("Cancel").Outline().OnClick(() =>
                          {
                              editContent.Set(originalContent.Value);
                              isEditing.Set(false);
                          });
            planTabContent = Layout.Vertical()
                            | editBar
                            | editContent.ToCodeInput()
                                .Language(Languages.Markdown)
                                .Width(Size.Full());
        }
        else
        {
            var planLayout = Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Full());
            if (selectedPlan.Status == PlanStatus.Failed) planLayout |= BuildFailureCallout(selectedPlan);
            var annotatedContent = MarkdownHelper.AnnotateAllBrokenLinks(selectedPlan.LatestRevisionContent, planService.PlansDirectory);
            planLayout |= new Markdown(annotatedContent)
                .DangerouslyAllowLocalFiles()
                .OnLinkClick(FileLinkHelper.CreateFileLinkClickHandler(openFile, planId =>
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
            planTabContent = planLayout;
        }

        if (planContentQuery.Loading)
        {
            content |= Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                       | Text.Muted("Loading...");
        }
        else
        {
            // Git tab content (uses shared helper — reuse precomputed commit rows from planContentQuery)
            var gitData = GitTabHelper.BuildGitTabData(planData.CommitRows, selectedPlan!, config, gitService);
            var gitLayout = GitTabHelper.RenderGitTab(
                gitData,
                selectedPlan!,
                client,
                config,
                hash => openCommit.Set(hash),
                path =>
                {
                    copyToClipboard(path);
                    client.Toast("Copied path to clipboard", "Path Copied");
                    return null!; // Return type doesn't matter, just need to satisfy Func
                }
            );

            // Changes tab
            var changesTabView = new ChangesTabView(planData.AllChanges, planContentQuery.Loading, planContentQuery.Error);

            // Artifacts tab content
            var totalArtifacts = (planData.Artifacts.GetValueOrDefault("screenshots")?.Count ?? 0)
                                 + (planData.Artifacts.ContainsKey("sample") ? 1 : 0);

            // Build tabs
            var tabs = Layout.Tabs(
                new Tab("Plan", Cap(planTabContent)),
                new Tab("Summary", Cap(new SummaryTabView(planData.SummaryMarkdown))),
                new Tab("Verifications", Cap(new VerificationsTabView(
                    selectedPlan.Verifications, planData.VerificationReports,
                    v => openVerification.Set(v)))).Badge(selectedPlan.Verifications.Count.ToString()),
                new Tab("Git", Cap(gitLayout)).Badge((gitData.WorktreeRows.Count + selectedPlan.Commits.Count + selectedPlan.Prs.Count).ToString()),
                new Tab("Changes", Cap(changesTabView)).Badge(changesTabView.FileCount > 0 ? changesTabView.FileCount.ToString() : ""),
                new Tab("Artifacts", Cap(new ArtifactsTabView(planData.Artifacts))).Badge(totalArtifacts.ToString())
            ).OnSelect(v => selectedTab.Set(v)).SelectedIndex(selectedTab.Value).Variant(TabsVariant.Content);

            content |= (Layout.Vertical().Padding(2).Height(Size.Full()) | tabs);
        }

        // Sheet modals
        content |= new VerificationReportSheet(openVerification, selectedPlan);
        content |= new CommitDetailSheet(openCommit, selectedPlan, config, gitService);

        // Check for active ExpandPlan job
        var hasActiveExpandJob = jobService.GetJobs().Any(j =>
            j.Type == "ExpandPlan" &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(selectedPlan.FolderPath, StringComparison.OrdinalIgnoreCase));

        // Check for active SplitPlan job
        var hasActiveSplitJob = jobService.GetJobs().Any(j =>
            j.Type == "SplitPlan" &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.Args.Length > 0 &&
            j.Args[0].Equals(selectedPlan.FolderPath, StringComparison.OrdinalIgnoreCase));

        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(1)
                        | new Button("Update").Icon(Icons.WandSparkles).Outline().ShortcutKey("u")
                            .OnClick(() => updateDialogOpen.Set(true))
                        | new Button("Edit").Icon(Icons.Pencil).Outline().ShortcutKey("E")
                            .OnClick(() => isEditing.Set(true))
                        | new Button("Split").Icon(Icons.Scissors).Outline().ShortcutKey("s")
                            .Disabled(hasActiveSplitJob)
                            .OnClick(() =>
                        {
                            if (hasActiveSplitJob) return;

                            // Optimistically update UI state before disk I/O
                            var optimisticPlan = selectedPlan with
                            {
                                Metadata = selectedPlan.Metadata with { State = PlanStatus.Updating }
                            };
                            selectedPlanState.Set(optimisticPlan);

                            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Updating);
                            jobService.StartJob("SplitPlan", selectedPlan.FolderPath);
                            refreshPlans();
                        })
                        | new Button("Expand").Icon(Icons.UnfoldVertical).Outline().ShortcutKey("x")
                            .Disabled(hasActiveExpandJob)
                            .OnClick(() =>
                        {
                            if (hasActiveExpandJob) return;

                            // Optimistically update UI state before disk I/O
                            var optimisticPlan = selectedPlan with
                            {
                                Metadata = selectedPlan.Metadata with { State = PlanStatus.Building }
                            };
                            selectedPlanState.Set(optimisticPlan);

                            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
                            var planPath = selectedPlan.FolderPath;
                            jobService.StartJob("ExpandPlan", planPath);
                            refreshPlans();
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
                                .OnSelect(() => { PlatformHelper.OpenInFileManager(selectedPlan.FolderPath); }),
                            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
                            {
                                PlatformHelper.OpenInTerminal(selectedPlan.FolderPath);
                            }),
                            new MenuItem($"Open in {config.Editor.Label}", Icon: Icons.Code, Tag: "OpenInEditor")
                                .OnSelect(() => { config.OpenInEditor(selectedPlan.FolderPath); }),
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    copyToClipboard(selectedPlan.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
                                }),
                            new MenuItem("Copy Plan to Clipboard", Icon: Icons.Share, Tag: "CopyPlan")
                                .OnSelect(() =>
                                {
                                    var exported = PlanExportHelper.ExportToClipboard(selectedPlan);
                                    copyToClipboard(exported);
                                    client.Toast("Plan copied to clipboard", "Plan Exported");
                                }),
                            new MenuItem("Mark as Completed", Icon: Icons.CircleCheck, Tag: "MarkCompleted")
                                .OnSelect(() =>
                                {
                                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Completed);
                                    refreshPlans();
                                }),
                            new MenuItem("Open plan.yaml", Icon: Icons.FileText, Tag: "OpenPlanYaml").OnSelect(() =>
                            {
                                var yamlPath = Path.Combine(selectedPlan.FolderPath, "plan.yaml");
                                config.OpenInEditor(yamlPath);
                            })
                        );

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                content
            ).Scroll(Scroll.None).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlan.Id);

        var elements = new List<object>
        {
            mainLayout,
            new UpdatePlanDialog(updateDialogOpen, updateText, selectedPlan, selectedPlanState, jobService, planService, refreshPlans),
            new DeletePlanDialog(deleteDialogOpen, selectedPlan, selectedPlanState, planService, refreshPlans),
            new CreateIssueDialog(createIssueDialogOpen, selectedRepoState, issueAssigneeState, issueLabelsState,
                issueCommentState, selectedPlan, jobService)
        };

        var repoPaths = selectedPlan.GetEffectiveRepoPaths(config);
        var fileLinkSheet = FileLinkHelper.BuildFileLinkSheet(
            openFile.Value, () => openFile.Set(null), repoPaths, config);
        if (fileLinkSheet is not null)
            elements.Add(fileLinkSheet);

        return new Fragment(elements.ToArray());

        object Cap(object inner) => Layout.Vertical().Width(Size.Auto().Max(Size.Units(200))).Height(Size.Full()) | inner;
    }

    internal static object BuildFailureCallout(PlanFile plan)
    {
        var verificationDir = Path.Combine(plan.FolderPath, "verification");
        var failedVerifications = plan.Verifications
            .Where(v => v.Status is "Fail" or "Pending")
            .ToList();

        if (failedVerifications.Count > 0 && Directory.Exists(verificationDir))
        {
            var parts = new List<string>();
            foreach (var v in failedVerifications)
            {
                var reportPath = Path.Combine(verificationDir, $"{v.Name}.md");
                if (!File.Exists(reportPath))
                {
                    parts.Add($"**{v.Name}** — {v.Status}, no report generated");
                    continue;
                }

                var report = FileHelper.ReadAllText(reportPath);

                // Extract the Output section content
                var outputMatch = Regex.Match(
                    report, @"## Output\s*\n([\s\S]*?)(?=\n## |\z)");
                var output = outputMatch.Success
                    ? outputMatch.Groups[1].Value.Trim()
                    : null;

                // Extract Issues Found section
                var issuesMatch = Regex.Match(
                    report, @"## Issues Found\s*\n([\s\S]*?)(?=\n## |\z)");
                var issues = issuesMatch.Success
                    ? issuesMatch.Groups[1].Value.Trim()
                    : null;

                var detail = output ?? issues ?? "See verification report for details";
                parts.Add($"**{v.Name}** — {detail}");
            }

            return Callout.Destructive(string.Join("\n\n", parts), "Execution Failed");
        }

        // Fall back to last execution log
        var logsDir = Path.Combine(plan.FolderPath, "logs");
        if (Directory.Exists(logsDir))
        {
            var lastLog = Directory.GetFiles(logsDir, "*.md")
                .OrderByDescending(f => f)
                .FirstOrDefault();
            if (lastLog != null)
            {
                var logContent = FileHelper.ReadAllText(lastLog);
                var summaryMatch = Regex.Match(
                    logContent, @"## Summary\s*\n([\s\S]*?)(?=\n## |\z)");
                if (summaryMatch.Success)
                    return Callout.Destructive(summaryMatch.Groups[1].Value.Trim(), "Execution Failed");

                var statusMatch = Regex.Match(
                    logContent, @"\*\*Status:\*\*\s*(.+)");
                if (statusMatch.Success)
                {
                    var status = statusMatch.Groups[1].Value.Trim();
                    if (status == "Completed")
                        return Callout.Warning(
                            "Execution reported as completed but plan is in Failed state. The process may have crashed during state transition.",
                            "State Mismatch");
                    return Callout.Destructive($"Last execution status: {status}", "Execution Failed");
                }
            }
        }

        return Callout.Destructive("No details available. Check the logs folder.", "Execution Failed");
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
