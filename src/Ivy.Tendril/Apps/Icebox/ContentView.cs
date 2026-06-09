using Ivy.Core;
using Ivy.Tendril.Apps.Icebox.Dialogs;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Apps.Views.Dialogs;
using Ivy.Tendril.Apps.Views.Sheets;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Icebox;

public class ContentView(
    PlanFile? selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IPlanReaderService planService,
    IJobService jobService,
    Action refreshPlans,
    IConfigService config) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();
        var openFile = UseState<string?>(null);
        var showDirtyDialog = UseState(false);
        var (runPreflight, isCheckingPreflight, preflightResult) = Context.UsePreflightCheck();

        var (deleteDialog, showDeleteDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new DeletePlanDialog(isOpen, selectedPlan!, planService, refreshPlans);
        });

        if (selectedPlan is null)
        {
            if (allPlans.Count == 0)
                return new NoContentView("Icebox is empty", "Plans you put on ice will appear here");

            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full())
                   | Text.Muted("Select a plan from the sidebar");
        }

        var currentIndex = allPlans.FindIndex(p => p.FolderName == selectedPlan.FolderName);

        var titleArea = Layout.Horizontal().Wrap().Gap(2).AlignContent(Align.Left).Width(Size.Grow())
                        | new Box(Text.Block($"#{selectedPlan.Id} {selectedPlan.Title}").Bold())
                            .BorderThickness(0).Padding(0)
                            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet)
                        | MobileItemPicker.Build(
                                $"#{selectedPlan.Id} {selectedPlan.Title}",
                                allPlans,
                                p => $"#{p.Id} {p.Title}",
                                p => p.FolderName == selectedPlan.FolderName,
                                p => selectedPlanState.Set(p))
                            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var controls = Layout.Horizontal().Gap(2).AlignContent(Align.Right).Width(Size.Grow())
                       | new Spacer().Width(Size.Grow())
                       | Text.Rich()
                           .Bold($"{currentIndex + 1}/{allPlans.Count}", word: true)
                           .Muted("plans", word: true);

        var header = Layout.Horizontal().Width(Size.Full()).Wrap().Gap(2).AlignContent(Align.Left)
                     | titleArea
                     | controls;

        var scrollableContent = Layout.Vertical().Width(Size.Full().Max(Size.Units(200)))
                                |
                                new Markdown(MarkdownHelper.AnnotateAllBrokenLinks(selectedPlan.LatestRevisionContent, planService.PlansDirectory))
                                    .Article()
                                    .DangerouslyAllowLocalFiles()
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

        var actionBar = Layout.Horizontal().AlignContent(Align.Left).Gap(1).Wrap()
                        | new Button("Delete").Icon(Icons.Trash).Outline().ShortcutKey("Backspace").OnClick(() => showDeleteDialog())
                        | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => GoToPrevious())
                            .ShortcutKey("p")
                        | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => GoToNext())
                            .ShortcutKey("n")
                        | new Button("Thaw").Icon(Icons.Flame).Primary().OnClick(() =>
                        {
                            planService.TransitionState(selectedPlan.FolderName, PlanStatus.Draft);
                            refreshPlans();
                        })
                        | new Button("Execute").Icon(Icons.Rocket).Outline().ShortcutKey("x")
                            .Loading(isCheckingPreflight)
                            .Disabled(isCheckingPreflight)
                            .OnClick(() => runPreflight(selectedPlan.Project, result =>
                            {
                                if (result.DirtyRepos.Count > 0)
                                    showDirtyDialog.Set(true);
                                else
                                    LaunchExecute();
                            }))
                        | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                            new MenuItem("Copy Path to Clipboard", Icon: Icons.ClipboardCopy, Tag: "CopyPath")
                                .OnSelect(() =>
                                {
                                    copyToClipboard(selectedPlan.FolderPath);
                                    client.Toast("Copied path to clipboard", "Path Copied");
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

        var mainContent = Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
                          | scrollableContent;

        var mainLayout = new HeaderLayout(
            header,
            new FooterLayout(
                actionBar,
                mainContent
            ).Size(Size.Full())
        ).Scroll(Scroll.None).Size(Size.Full()).Key(selectedPlan.Id);

        var dirtyRepoDialog = showDirtyDialog.Value && preflightResult is { DirtyRepos.Count: > 0 }
            ? new DirtyRepoDialog(
                showDirtyDialog,
                preflightResult,
                proceedLabel: "Execute Anyway",
                contextMessage: "These changes will NOT be included in this plan. The plan will execute against origin/<baseBranch>. If these changes are meant for this plan, commit and push them first.",
                onSyncRepos: () => LaunchWithSync(preflightResult),
                onProceed: LaunchExecute)
            : null;

        var elements = new List<object>
        {
            mainLayout,
            deleteDialog
        };

        if (dirtyRepoDialog is not null)
            elements.Add(dirtyRepoDialog);

        elements.Add(new FileSheet(openFile, config));

        return new Fragment(elements.ToArray());
    }

    private void LaunchExecute()
    {
        if (selectedPlan is null) return;
        planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
        jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath));
        refreshPlans();
    }

    private void LaunchWithSync(PreflightResult preflight)
    {
        if (selectedPlan is null) return;

        var syncJobIds = new List<string>();
        foreach (var (repoPath, baseBranch, _) in preflight.DirtyRepos)
        {
            var jobId = jobService.StartJob(new SyncRepoArgs(repoPath, baseBranch, selectedPlan.FolderPath));
            syncJobIds.Add(jobId);
        }

        planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
        jobService.StartJob(new ExecutePlanArgs(selectedPlan.FolderPath) { WaitForJobs = syncJobIds });
        refreshPlans();
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
}
