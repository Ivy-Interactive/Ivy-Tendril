using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Core;

namespace Ivy.Tendril.Apps.Plans;

public class ActionBarView(
    PlanFile selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IState<bool> isEditingState,
    IState<string> editContentState,
    IState<string> originalContentState,
    int pendingAdjustmentCount,
    bool hasActiveUpdateJob,
    Action submitAdjustments,
    IState<bool> deleteDialogOpenState,
    IState<bool> createIssueDialogOpenState,
    IPlanReaderService planService,
    IJobService jobService,
    IConfigService config,
    Action refreshPlans,
    Action<string> copyToClipboard,
    bool hasActiveExpandJob,
    bool hasActiveSplitJob,
    Action goToNext,
    Action goToPrevious,
    string downloadUrl) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        if (isEditingState.Value)
        {
            // Edit mode: show only Save and Cancel buttons
            return Layout.Horizontal().AlignContent(Align.Left).Gap(1)
                | new Button("Save Revision").Icon(Icons.Save).Primary().ShortcutKey("S").OnClick(() =>
                {
                    if (editContentState.Value != originalContentState.Value)
                    {
                        planService.SaveRevision(selectedPlan.FolderName, editContentState.Value);
                        var updated = planService.GetPlanByFolder(selectedPlan.FolderPath);
                        if (updated != null) selectedPlanState.Set(updated);
                        refreshPlans();
                    }
                    isEditingState.Set(false);
                })
                | new Button("Cancel").Outline().ShortcutKey("Escape").OnClick(() =>
                {
                    editContentState.Set(originalContentState.Value);
                    isEditingState.Set(false);
                });
        }
        else
        {
            // Normal mode: show all action buttons (Edit first)
            return Layout.Horizontal().AlignContent(Align.Left).Gap(1)
                | new Button("Edit").Icon(Icons.Pencil).Outline().ShortcutKey("E")
                    .OnClick(() => isEditingState.Set(true))
                | new Button("Update").Icon(Icons.WandSparkles).Outline().ShortcutKey("u")
                    .Badge(pendingAdjustmentCount > 0 ? pendingAdjustmentCount.ToString() : null)
                    .Disabled(pendingAdjustmentCount == 0 || hasActiveUpdateJob)
                    .OnClick(() => submitAdjustments())
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
                    .OnClick(() => deleteDialogOpenState.Set(true))
                | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(() => goToPrevious())
                    .ShortcutKey("p")
                | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(() => goToNext())
                    .ShortcutKey("n")
                | new Button().Icon(Icons.EllipsisVertical).Ghost().WithDropDown(
                    new MenuItem("Create Issue", Icon: Icons.Github, Tag: "CreateIssue").OnSelect(() =>
                        createIssueDialogOpenState.Set(true)),
                    new MenuItem("Download", Icon: Icons.Download, Tag: "Download").OnSelect(() =>
                    {
                        if (!string.IsNullOrEmpty(downloadUrl)) client.OpenUrl(downloadUrl);
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
        }
    }
}
