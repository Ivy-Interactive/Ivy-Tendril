using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Share;
using Ivy.Tendril.Services.Tunnel;

namespace Ivy.Tendril.Apps.Plans;

public sealed record DraftActionsContext(
    PlanFile Plan,
    IState<PlanFile?> SelectedPlanState,
    IState<bool> IsEditing,
    IState<string> EditContent,
    IState<string> OriginalContent,
    IPlanReaderService PlanService,
    IJobService JobService,
    IConfigService Config,
    IClientProvider Client,
    INavigator Nav,
    IAgentRunner AgentRunner,
    IShareContext ShareContext,
    IShareTunnelService ShareTunnelService,
    bool IsBeta,
    Action RefreshPlans,
    Action<string> CopyToClipboard,
    Action ShowDeleteDialog,
    Action ShowCreateIssueDialog,
    Action ShowShareModal,
    bool HasActiveExpandJob,
    bool HasActiveSplitJob);

/// <summary>
///     What the Drafts page can do with a plan, laid out for the workspace top bar: Edit, Update
///     (which hands the caret to the chat composer), Expand and Share as icons, everything else in
///     the overflow menu. Edit mode and share mode each replace the set with their own.
/// </summary>
public static class DraftActions
{
    public const string ChatTag = "Chat";

    public static PlanWorkspaceActions Build(DraftActionsContext ctx)
    {
        var actions = new PlanWorkspaceActions();
        var plan = ctx.Plan;
        var config = ctx.Config;
        var client = ctx.Client;
        var (agentLabel, agentIcon) = AgentBranding.For(config.Settings.CodingAgent, ctx.AgentRunner, config);

        void SharePlan()
        {
            if (ctx.ShareTunnelService.IsConnected && !string.IsNullOrEmpty(ctx.ShareTunnelService.TunnelUrl))
            {
                var link = ctx.ShareTunnelService.GetShareUrlForPlan(plan.FolderName, isReview: false);
                ctx.CopyToClipboard(link);
                client.Toast("Plan share link copied to clipboard", "Link Copied");
            }
            else
            {
                ctx.ShowShareModal();
            }
        }

        void CopyPlan()
        {
            ctx.CopyToClipboard(PlanExportHelper.ExportToClipboard(plan));
            client.Toast("Plan copied to clipboard", "Plan Exported");
        }

        void OpenInEditor(string path)
        {
            try
            {
                config.OpenInEditor(path);
            }
            catch (EditorNotAvailableException ex)
            {
                client.Toast(
                    $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                    "Editor Not Available",
                    variant: ToastVariant.Destructive);
            }
        }

        if (ctx.ShareContext.IsShareMode)
        {
            return actions
                .Action("SharePlan", "Share Plan", Icons.Share2, SharePlan)
                .Action("CopyPlan", "Copy Plan", Icons.ClipboardCopy, CopyPlan);
        }

        if (ctx.IsEditing.Value)
        {
            return actions
                .SetPrimary("SaveRevision", "Save Revision", Icons.Save, () =>
                {
                    if (ctx.EditContent.Value != ctx.OriginalContent.Value)
                    {
                        ctx.PlanService.SaveRevision(plan.FolderName, ctx.EditContent.Value);
                        var updated = ctx.PlanService.GetPlanByFolder(plan.FolderPath);
                        if (updated != null) ctx.SelectedPlanState.Set(updated);
                        ctx.RefreshPlans();
                    }
                    ctx.IsEditing.Set(false);
                }, "S")
                .AddSecondary("CancelEdit", "Cancel", null, () =>
                {
                    ctx.EditContent.Set(ctx.OriginalContent.Value);
                    ctx.IsEditing.Set(false);
                }, "Escape");
        }

        void StartSplit()
        {
            if (ctx.HasActiveSplitJob) return;
            ctx.SelectedPlanState.Set(plan with { Metadata = plan.Metadata with { State = PlanStatus.Updating } });
            // Plan state transition (and pre-state snapshot) handled by JobService.StartJob.
            ctx.JobService.StartJob(new SplitPlanArgs(plan.FolderPath));
            ctx.RefreshPlans();
        }

        void StartExpand()
        {
            if (ctx.HasActiveExpandJob) return;
            ctx.SelectedPlanState.Set(plan with { Metadata = plan.Metadata with { State = PlanStatus.Creating } });
            ctx.JobService.StartJob(new ExpandPlanArgs(plan.FolderPath));
            ctx.RefreshPlans();
        }

        actions
            .Action("Edit", "Edit", Icons.Pencil, () => ctx.IsEditing.Set(true), "E")
            .Action(ChatTag, "Update", Icons.WandSparkles, () => { }, "U", focusChat: true)
            .Action("Expand", "Expand", Icons.Expand, StartExpand, "P", disabled: ctx.HasActiveExpandJob);

        if (ctx.IsBeta)
            actions.Action("Share", "Share", Icons.Share2, SharePlan);

        return actions
            .Menu("Split", "Split", Icons.Scissors, StartSplit, disabled: ctx.HasActiveSplitJob)
            .Menu("Delete", "Delete", Icons.Trash, ctx.ShowDeleteDialog, "Backspace", danger: true)
            .Menu("CreateIssue", "Create Issue", Icons.Github, ctx.ShowCreateIssueDialog)
            .Menu("DiscussWithAgent", $"Discuss with {agentLabel}", agentIcon, () => ctx.Nav.Navigate<AgentApp>(new AgentAppArgs(
                $"User wants to discuss the plan {plan.FolderPath} currently in Draft mode.",
                $"#{TendrilAppShell.FormatPlanId(plan.FolderName)}")))
            .Menu("OpenInExplorer", "Open in File Manager", Icons.FolderOpen, () => PlatformHelper.OpenInFileManager(plan.FolderPath))
            .Menu("OpenInTerminal", "Open in Terminal", Icons.Terminal, () => PlatformHelper.OpenInTerminal(plan.FolderPath))
            .Menu("OpenInEditor", $"Open in {config.Editor.Label}", Icons.Code, () => OpenInEditor(plan.FolderPath))
            .Menu("CopyPath", "Copy Path to Clipboard", Icons.ClipboardCopy, () =>
            {
                ctx.CopyToClipboard(plan.FolderPath);
                client.Toast("Copied path to clipboard", "Path Copied");
            })
            .Menu("CopyPlan", "Copy Plan to Clipboard", Icons.Share, CopyPlan)
            .Menu("OpenPlanYaml", "Open plan.yaml", Icons.FileText, () => OpenInEditor(Path.Combine(plan.FolderPath, "plan.yaml")));
    }
}
