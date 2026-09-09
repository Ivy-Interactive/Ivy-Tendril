using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Share;
using Ivy.Tendril.Services.Tunnel;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Apps.Review;

public sealed record ReviewActionsContext(
    PlanFile Plan,
    IConfigService Config,
    IClientProvider Client,
    ILogger Logger,
    INavigator Nav,
    IAgentRunner AgentRunner,
    IShareContext ShareContext,
    IShareTunnelService ShareTunnelService,
    bool IsBeta,
    Action<string> CopyToClipboard,
    int CommentCount,
    Action ShowResetToDraftDialog,
    Action ShowSuggestChangesDialog,
    Action ShowDiscardDialog,
    Action ShowShareModal);

/// <summary>
///     What the Review page can do with a plan, laid out for the workspace top bar: Reset to Draft,
///     Request Changes, Ask Tendril (the caret goes to the chat composer) and Share as icons, the
///     rest in the overflow menu.
/// </summary>
public static class ReviewActions
{
    public const string ChatTag = "Chat";

    public static PlanWorkspaceActions Build(ReviewActionsContext ctx)
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
                var link = ctx.ShareTunnelService.GetShareUrlForPlan(plan.FolderName, isReview: true);
                ctx.CopyToClipboard(link);
                client.Toast("Plan share link copied to clipboard", "Link Copied");
            }
            else
            {
                ctx.ShowShareModal();
            }
        }

        void CopyPath()
        {
            ctx.CopyToClipboard(plan.FolderPath);
            client.Toast("Copied path to clipboard", "Path Copied");
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
                .Action("CopyPath", "Copy Path", Icons.Copy, CopyPath);
        }

        actions
            .Action("ResetToDraft", "Reset to Draft", Icons.RotateCcw, ctx.ShowResetToDraftDialog, "r")
            .Action("RequestChanges", "Request Changes", Icons.MessageSquare, ctx.ShowSuggestChangesDialog, "c",
                badge: ctx.CommentCount > 0 ? ctx.CommentCount.ToString() : null)
            .Action(ChatTag, "Ask Tendril", Icons.WandSparkles, () => { }, "u", focusChat: true);

        if (ctx.IsBeta)
            actions.Action("Share", "Share", Icons.Share2, SharePlan);

        return actions
            .Menu("Discard", "Discard", Icons.Trash, ctx.ShowDiscardDialog, "Backspace", danger: true)
            .Menu("DiscussWithAgent", $"Discuss with {agentLabel}", agentIcon, () => ctx.Nav.Navigate<AgentApp>(new AgentAppArgs(
                $"User wants to discuss the plan {plan.FolderPath} currently in Review mode.",
                $"#{TendrilAppShell.FormatPlanId(plan.FolderName)}")))
            .Menu("OpenInExplorer", "Open in File Manager", Icons.FolderOpen, () => PlatformHelper.OpenInFileManager(plan.FolderPath, ctx.Logger))
            .Menu("OpenInTerminal", "Open in Terminal", Icons.Terminal, () => PlatformHelper.OpenInTerminal(plan.FolderPath, ctx.Logger))
            .Menu("CopyPath", "Copy Path", Icons.Copy, CopyPath)
            .Menu("OpenInEditor", $"Open in {config.Editor.Label}", Icons.Code, () => OpenInEditor(plan.FolderPath))
            .Menu("OpenPlanYaml", "Open plan.yaml", Icons.FileText, () => OpenInEditor(Path.Combine(plan.FolderPath, "plan.yaml")));
    }
}
