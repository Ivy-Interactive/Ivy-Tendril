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

        // Split / Expand share the same OnClick logic whether shown as an inline button
        // (Full tier) or a dropdown item (Compact/Minimal tiers).
        void StartSplit()
        {
            if (hasActiveSplitJob) return;
            var optimisticPlan = selectedPlan with
            {
                Metadata = selectedPlan.Metadata with { State = PlanStatus.Updating }
            };
            selectedPlanState.Set(optimisticPlan);
            // Plan state transition (and pre-state snapshot) handled by JobService.StartJob.
            jobService.StartJob(new SplitPlanArgs(selectedPlan.FolderPath));
            refreshPlans();
        }

        void StartExpand()
        {
            if (hasActiveExpandJob) return;
            var optimisticPlan = selectedPlan with
            {
                Metadata = selectedPlan.Metadata with { State = PlanStatus.Creating }
            };
            selectedPlanState.Set(optimisticPlan);
            // Plan state transition (and pre-state snapshot) handled by JobService.StartJob.
            jobService.StartJob(new ExpandPlanArgs(selectedPlan.FolderPath));
            refreshPlans();
        }

        // Compact-tier dropdown items: buttons that don't fit at the Compact tier + standard overflow
        var compactDropdownItems = new List<MenuItem>
        {
            new MenuItem("Split", Icon: Icons.Scissors, Tag: "Split")
                .OnSelect(StartSplit).Disabled(hasActiveSplitJob),
            new MenuItem("Expand", Icon: Icons.UnfoldVertical, Tag: "Expand")
                .OnSelect(StartExpand).Disabled(hasActiveExpandJob),
            new MenuItem("Delete", Icon: Icons.Trash, Tag: "Delete").OnSelect(showDeleteDialog)
        };
        compactDropdownItems.AddRange(standardOverflowItems);

        // Minimal-tier dropdown items: all action buttons + standard overflow
        var minimalDropdownItems = new List<MenuItem>
        {
            new MenuItem("Edit", Icon: Icons.Pencil, Tag: "Edit")
                .OnSelect(() => isEditingState.Set(true)),
            new MenuItem("Update", Icon: Icons.WandSparkles, Tag: "Update")
                .OnSelect(showUpdateDialog),
            new MenuItem("Split", Icon: Icons.Scissors, Tag: "Split")
                .OnSelect(StartSplit).Disabled(hasActiveSplitJob),
            new MenuItem("Expand", Icon: Icons.UnfoldVertical, Tag: "Expand")
                .OnSelect(StartExpand).Disabled(hasActiveExpandJob),
            new MenuItem("Delete", Icon: Icons.Trash, Tag: "Delete").OnSelect(showDeleteDialog)
        };
        minimalDropdownItems.AddRange(standardOverflowItems);

        // Action bar without .Wrap() - single row layout with progressive collapse.
        // Full tier (>=1024px): all buttons inline + overflow-only dropdown.
        // Compact tier (768-1023px): Previous, Next, Edit, Update inline; Split/Expand/Delete in dropdown.
        // Minimal tier (<768px): Previous, Next inline; everything else in dropdown.
        return Layout.Horizontal().AlignContent(Align.Left).Gap(2)
               | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(goToPrevious)
                   .ShortcutKey("p").AlwaysVisible()
               | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(goToNext)
                   .ShortcutKey("n").AlwaysVisible()
               | new Button("Edit").Icon(Icons.Pencil).Outline().ShortcutKey("E")
                   .OnClick(() => isEditingState.Set(true)).CompactUp()
               | new Button("Update").Icon(Icons.WandSparkles).Outline().ShortcutKey("u")
                   .OnClick(showUpdateDialog).CompactUp()
               | new Button("Split").Icon(Icons.Scissors).Outline()
                   .OnClick(StartSplit).Disabled(hasActiveSplitJob).FullOnly()
               | new Button("Expand").Icon(Icons.UnfoldVertical).Outline()
                   .OnClick(StartExpand).Disabled(hasActiveExpandJob).FullOnly()
               | new Button("Delete").Icon(Icons.Trash).Outline()
                   .OnClick(showDeleteDialog).FullOnly()
               // Full-tier dropdown: standard overflow items only
               | ActionBarResponsive.DropdownAtFull(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   standardOverflowItems)
               // Compact-tier dropdown: Split, Expand, Delete + standard overflow
               | ActionBarResponsive.DropdownAtCompact(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   compactDropdownItems.ToArray())
               // Minimal-tier dropdown: all action buttons + standard overflow
               | ActionBarResponsive.DropdownAtMinimal(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   minimalDropdownItems.ToArray());
    }
}
