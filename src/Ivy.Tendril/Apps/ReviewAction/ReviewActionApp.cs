using System.Diagnostics;
using System.Reactive.Disposables;
using Ivy.Hooks.Pty;
using Ivy.Tendril.Agents.Helpers;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Widgets.Xterm;
using Xterm = Ivy.Widgets.Xterm;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     Hosts a review action's PowerShell command in a PTY terminal, opened as its own tab
///     (see <see cref="AppShell.TendrilAppShell.ResolveArgsTabTitle"/> for the tab title).
///     Modelled on <see cref="Agent.AgentApp"/>, but without trust-prompt handling or agent
///     config: the command is re-resolved server-side from the plan's project config so it
///     never travels through navigation args.
/// </summary>
[App(title: "Review Action", icon: Icons.Play, isVisible: false, order: Constants.ReviewAction, allowDuplicateTabs: true)]
public class ReviewActionApp : ViewBase
{
    public override object Build()
    {
        var configService = UseService<IConfigService>();
        var planService = UseService<IPlanReaderService>();
        var args = UseArgs<ReviewActionAppArgs>();

        // Ivy hooks must come first (IVYHOOK005), so the plan/action lookup that determines the
        // command line and working directory happens inside GetCommandLine/GetWorkDir (called as
        // direct arguments, like AgentApp does), not as statements preceding UsePty. When nothing
        // resolves, GetCommandLine returns an empty array, which makes UsePty's StartPtyAsync a
        // no-op, so nothing is spawned.
        var ptyHandle = Context.UsePty(
            GetCommandLine(configService, planService, args),
            GetWorkDir(planService, args));

        // Windows job-object teardown (which UsePty relies on to kill the whole process tree on
        // disposal) does not reliably reach every grandchild - verified by spawning a pwsh -> a
        // long-running grandchild and observing the grandchild survive a plain pty.Kill(). So this
        // app also kills the process tree by pid explicitly when the tab closes, as a backstop.
        UseEffect(() => Disposable.Create(() =>
        {
            if (ptyHandle.GetProcessId?.Invoke() is not { } pid) return;
            try
            {
                ProcessRunner.KillProcessTree(Process.GetProcessById(pid));
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }), EffectTrigger.OnMount());

        var plan = ResolvePlan(planService, args?.PlanId);
        if (plan is null)
        {
            return Text.Muted("Plan not found.");
        }

        var action = ResolveAction(configService, plan.Project, args?.ActionName);
        if (action is null)
        {
            return Text.Muted($"Review action \"{args?.ActionName}\" is not configured for project \"{plan.Project}\".");
        }

        return new Xterm.Terminal()
            .Stream(ptyHandle.Stream)
            .OnInput(ptyHandle.HandleInput)
            .OnResize(ptyHandle.HandleResize)
            .Closed(ptyHandle.Closed)
            .AllowClipboard()
            .Loading($"Starting {action.Name}...")
            .WithLayout()
            .Full()
            .RemoveParentPadding();
    }

    private static string[] GetCommandLine(IConfigService configService, IPlanReaderService planService, ReviewActionAppArgs? args)
    {
        var plan = ResolvePlan(planService, args?.PlanId);
        var action = plan is not null ? ResolveAction(configService, plan.Project, args?.ActionName) : null;
        return action is not null
            ? [PathHelper.GetPwshPath(), "-NoExit", "-NoProfile", "-Command", action.Command]
            : [];
    }

    private static string? GetWorkDir(IPlanReaderService planService, ReviewActionAppArgs? args) =>
        ResolvePlan(planService, args?.PlanId)?.FolderPath;

    private static PlanFile? ResolvePlan(IPlanReaderService planService, string? planId)
    {
        if (string.IsNullOrEmpty(planId)) return null;
        var folder = Path.Combine(planService.PlansDirectory, planId);
        return planService.GetPlanByFolder(folder);
    }

    /// <summary>
    ///     Resolves a review action's command by name from the project config. Returns null
    ///     (so the caller renders an explanation instead of spawning <c>pwsh</c>) when the
    ///     project, action, or its command is missing.
    /// </summary>
    internal static ReviewActionConfig? ResolveAction(IConfigService config, string? project, string? actionName)
    {
        if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(actionName)) return null;
        var action = config.GetProject(project)?.ReviewActions.FirstOrDefault(a => a.Name == actionName);
        return string.IsNullOrEmpty(action?.Command) ? null : action;
    }
}
