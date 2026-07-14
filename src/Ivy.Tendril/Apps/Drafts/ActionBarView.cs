using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Agent;
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
        var nav = UseNavigation();
        var agentRunner = UseService<IAgentRunner>();
        var (agentLabel, agentIcon) = AgentBranding.For(config.Settings.CodingAgent, agentRunner);

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
            new MenuItem($"Discuss with {agentLabel}", Icon: agentIcon, Tag: "DiscussWithAgent")
                .OnSelect(() => nav.Navigate<AgentApp>(new AgentAppArgs(
                    $"User wants to discuss the plan {selectedPlan.FolderPath} currently in Draft mode."))),
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

        // Pane-Compact-tier dropdown items: buttons that never get an inline slot in the
        // content pane (Split/Expand/Delete) + standard overflow. See the pane-width note
        // on ActionBarResponsive: the Draft footer's content pane is narrower than the
        // viewport by the sidebar + drafts-list pane to its left, so even at the Wide
        // viewport tier only Previous/Next/Edit/Update safely fit inline.
        var paneCompactDropdownItems = new List<MenuItem>
        {
            new MenuItem("Split", Icon: Icons.Scissors, Tag: "Split")
                .OnSelect(StartSplit).Disabled(hasActiveSplitJob),
            new MenuItem("Expand", Icon: Icons.UnfoldVertical, Tag: "Expand")
                .OnSelect(StartExpand).Disabled(hasActiveExpandJob),
            new MenuItem("Delete", Icon: Icons.Trash, Tag: "Delete").OnSelect(showDeleteDialog)
        };
        paneCompactDropdownItems.AddRange(standardOverflowItems);

        // Pane-Minimal-tier dropdown items: all action buttons + standard overflow
        var paneMinimalDropdownItems = new List<MenuItem>
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
        paneMinimalDropdownItems.AddRange(standardOverflowItems);

        // Action bar without .Wrap() - the footer slot is fixed-height, so wrapping could
        // push content out; a single row with progressive collapse is required instead.
        //
        // These tiers are keyed to the CONTENT PANE, not the raw viewport — see the
        // pane-width note on ActionBarResponsive. The pane is narrower than the viewport
        // by the app sidebar + drafts-list pane to its left, so the budget for each tier
        // is shifted down by one viewport step from what a full-width bar would use:
        // Pane-Compact tier (viewport >=1024px / Wide): Previous, Next, Edit, Update inline; Split/Expand/Delete in dropdown.
        // Pane-Minimal tier (viewport <1024px): Previous, Next inline; everything else in dropdown.
        // Split/Expand/Delete never get an inline slot at any viewport width — the pane is
        // never wide enough to guarantee they fit, so they're always dropdown-only.
        return Layout.Horizontal().AlignContent(Align.Left).Gap(2)
               | new Button("Previous").Icon(Icons.ChevronLeft).Outline().OnClick(goToPrevious)
                   .ShortcutKey("p").AlwaysVisible()
               | new Button("Next").Icon(Icons.ChevronRight, Align.Right).Outline().OnClick(goToNext)
                   .ShortcutKey("n").AlwaysVisible()
               | new Button("Edit").Icon(Icons.Pencil).Outline().ShortcutKey("E")
                   .OnClick(() => isEditingState.Set(true)).PaneCompactUp()
               | new Button("Update").Icon(Icons.WandSparkles).Outline().ShortcutKey("u")
                   .OnClick(showUpdateDialog).PaneCompactUp()
               // Pane-Compact-tier dropdown: Split, Expand, Delete + standard overflow
               | ActionBarResponsive.DropdownAtPaneCompact(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   paneCompactDropdownItems.ToArray())
               // Pane-Minimal-tier dropdown: all action buttons + standard overflow
               | ActionBarResponsive.DropdownAtPaneMinimal(
                   new Button().Icon(Icons.EllipsisVertical).Ghost(),
                   paneMinimalDropdownItems.ToArray());
    }
}
