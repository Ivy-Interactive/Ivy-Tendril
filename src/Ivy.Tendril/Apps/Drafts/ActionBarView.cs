using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Drafts;

public class ActionBarView(
    PlanFile selectedPlan,
    List<PlanFile> allPlans,
    IState<PlanFile?> selectedPlanState,
    IState<bool> isEditingState,
    IState<string> editContentState,
    IState<string> originalContentState,
    Action showUpdateDialog,
    Action showDeleteDialog,
    Action showCreateIssueDialog,
    IPlanReaderService planService,
    IJobService jobService,
    IConfigService config,
    Action refreshPlans,
    Action<string> copyToClipboard,
    bool hasActiveExpandJob,
    bool hasActiveSplitJob,
    Action goToNext,
    Action goToPrevious) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();

        if (isEditingState.Value)
        {
            // Edit mode: show only Save and Cancel buttons
            return Layout.Horizontal().AlignContent(Align.Left).Gap(2).Wrap()
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

        // Standard overflow menu items (always at bottom of dropdowns)
        var standardOverflowItems = new[]
        {
            new MenuItem("Create Issue", Icon: Icons.Github, Tag: "CreateIssue").OnSelect(showCreateIssueDialog),
            new MenuItem("Open in File Manager", Icon: Icons.FolderOpen, Tag: "OpenInExplorer")
                .OnSelect(() => { PlatformHelper.OpenInFileManager(selectedPlan.FolderPath); }),
            new MenuItem("Open in Terminal", Icon: Icons.Terminal, Tag: "OpenInTerminal").OnSelect(() =>
            {
                PlatformHelper.OpenInTerminal(selectedPlan.FolderPath);
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

        // Desktop dropdown items: buttons that don't fit at Desktop tier + standard overflow
        var desktopDropdownItems = new List<MenuItem>
        {
            new MenuItem("Split", Icon: Icons.Scissors, Tag: "Split")
                .OnSelect(() =>
                {
                    if (hasActiveSplitJob) return;
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Updating }
                    };
                    selectedPlanState.Set(optimisticPlan);
                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Updating);
                    jobService.StartJob(new SplitPlanArgs(selectedPlan.FolderPath));
                    refreshPlans();
                })
                .Disabled(hasActiveSplitJob),
            new MenuItem("Expand", Icon: Icons.UnfoldVertical, Tag: "Expand")
                .OnSelect(() =>
                {
                    if (hasActiveExpandJob) return;
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Building }
                    };
                    selectedPlanState.Set(optimisticPlan);
                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
                    var planPath = selectedPlan.FolderPath;
                    jobService.StartJob(new ExpandPlanArgs(planPath));
                    refreshPlans();
                })
                .Disabled(hasActiveExpandJob),
            new MenuItem("Delete", Icon: Icons.Trash, Tag: "Delete").OnSelect(showDeleteDialog)
        };
        desktopDropdownItems.AddRange(standardOverflowItems);

        // Mobile dropdown items: all action buttons + standard overflow
        var mobileDropdownItems = new List<MenuItem>
        {
            new MenuItem("Edit", Icon: Icons.Pencil, Tag: "Edit")
                .OnSelect(() => isEditingState.Set(true)),
            new MenuItem("Update", Icon: Icons.WandSparkles, Tag: "Update")
                .OnSelect(showUpdateDialog),
            new MenuItem("Split", Icon: Icons.Scissors, Tag: "Split")
                .OnSelect(() =>
                {
                    if (hasActiveSplitJob) return;
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Updating }
                    };
                    selectedPlanState.Set(optimisticPlan);
                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Updating);
                    jobService.StartJob(new SplitPlanArgs(selectedPlan.FolderPath));
                    refreshPlans();
                })
                .Disabled(hasActiveSplitJob),
            new MenuItem("Expand", Icon: Icons.UnfoldVertical, Tag: "Expand")
                .OnSelect(() =>
                {
                    if (hasActiveExpandJob) return;
                    var optimisticPlan = selectedPlan with
                    {
                        Metadata = selectedPlan.Metadata with { State = PlanStatus.Building }
                    };
                    selectedPlanState.Set(optimisticPlan);
                    planService.TransitionState(selectedPlan.FolderName, PlanStatus.Building);
                    var planPath = selectedPlan.FolderPath;
                    jobService.StartJob(new ExpandPlanArgs(planPath));
                    refreshPlans();
                })
                .Disabled(hasActiveExpandJob),
            new MenuItem("Delete", Icon: Icons.Trash, Tag: "Delete").OnSelect(showDeleteDialog)
        };
        mobileDropdownItems.AddRange(standardOverflowItems);

        // Action bar without .Wrap() - single row layout with progressive collapse
        return Layout.Horizontal().AlignContent(Align.Left).Gap(2)
               // Desktop: Previous, Next, Edit, Update visible
               | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(goToPrevious)
                   .ShortcutKey("p").AlwaysVisible()
               | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(goToNext)
                   .ShortcutKey("n").AlwaysVisible()
               | new Button("Edit").Icon(Icons.Pencil).Outline().ShortcutKey("E")
                   .OnClick(() => isEditingState.Set(true)).DesktopUp()
               | new Button("Update").Icon(Icons.WandSparkles).Outline().ShortcutKey("u")
                   .OnClick(showUpdateDialog).DesktopUp()
               // Desktop dropdown: Split, Expand, Delete + standard overflow
               | ActionBarResponsive.DropdownAtDesktop(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   desktopDropdownItems.ToArray())
               // Mobile dropdown: all action buttons + standard overflow
               | ActionBarResponsive.DropdownAtMobile(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   mobileDropdownItems.ToArray());
    }
}
