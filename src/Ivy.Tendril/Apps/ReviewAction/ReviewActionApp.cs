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
        // var ptyHandleRef = UseRef<PtyHandle?>(); // Commented out - not used until PtyHandle.GetProcessId is available

        // TODO: Explicit process tree kill disabled - PtyHandle does not expose GetProcessId.
        // UsePty's cleanup (pty.Kill/Dispose) should handle process termination, though Windows
        // job-object teardown may not reliably reach every grandchild. Requires PtyHandle update
        // to expose process ID if this backstop is needed.
        //
        // Windows job-object teardown (which UsePty relies on to kill the whole process tree on
        // disposal) does not reliably reach every grandchild - verified by spawning a pwsh -> a
        // long-running grandchild and observing the grandchild survive a plain pty.Kill(). So this
        // app also kills the process tree by pid explicitly when the tab closes, as a backstop.
        //
        // This effect is registered BEFORE Context.UsePty() below so its cleanup runs first when
        // the tab closes (effect cleanups run in registration order): by the time UsePty's own
        // cleanup calls pty.Kill()/Dispose(), the tree is already gone, instead of the reverse -
        // where UsePty kills the parent first and Process.GetProcessById(pid) below would throw
        // because the pid no longer exists, silently skipping the grandchild kill entirely.
        // ptyHandleRef bridges the value across, since the handle itself doesn't exist yet at
        // this point in Build() (Ivy hooks must come first, IVYHOOK005, so it can't be resolved
        // via a preceding non-hook statement either).
        // UseEffect(() => Disposable.Create(() =>
        // {
        //     if (ptyHandleRef.Value?.GetProcessId?.Invoke() is not { } pid) return;
        //     try
        //     {
        //         ProcessRunner.KillProcessTree(Process.GetProcessById(pid));
        //     }
        //     catch (ArgumentException)
        //     {
        //         // Process already exited.
        //     }
        // }), EffectTrigger.OnMount());

        // The plan/action lookup happens once via UseMemo (itself a hook, so this still satisfies
        // IVYHOOK005's "hooks must come first" rule - it can't be a plain statement preceding
        // UsePty). GetCommandLine then derives its argv from the already-resolved tuple instead
        // of re-running ResolvePlan/ResolveAction. When nothing resolves, GetCommandLine returns
        // an empty array, which makes UsePty's StartPtyAsync a no-op, so nothing is spawned.
        var (plan, action) = UseMemo(() =>
        {
            var resolvedPlan = ResolvePlan(planService, args?.PlanId);
            var resolvedAction = resolvedPlan is not null
                ? ResolveAction(configService, resolvedPlan.Project, args?.ActionName)
                : null;
            return (resolvedPlan, resolvedAction);
        });

        // CaptureOutput is what lets the URL below be found at all: the transcript is read
        // server-side, so the app under review is picked up from its own startup banner rather
        // than waiting for the reviewer to spot a link and click it inside the terminal.
        var ptyHandle = Context.UsePty(
            GetCommandLine(plan, action),
            plan?.FolderPath,
            new PtyOptions { CaptureOutput = true });
        // ptyHandleRef.Value = ptyHandle; // Commented out - ptyHandleRef not used

        // Set once and then left alone. A dev server prints its banner again on every hot
        // restart, and prints a second URL for its network address; re-pointing the viewer on
        // either would throw away wherever the reviewer had navigated to, mid-review.
        var appUrl = UseState<string?>(() => null);
        Context.UseInterval(
            () =>
            {
                if (appUrl.Value is not null) return;
                if (AppPreview.DetectUrl(AnsiEscape.Strip(ptyHandle.Output)) is { } url) appUrl.Set(url);
            },
            TimeSpan.FromMilliseconds(500));

        if (plan is null)
        {
            return Text.Muted("Plan not found.");
        }

        if (action is null)
        {
            return Text.Muted($"Review action \"{args?.ActionName}\" is not configured for project \"{plan.Project}\".");
        }

        // The terminal is the start of a review action, not the point of one: as soon as the
        // process says where it is serving, the tab becomes that app — framed, proxied and
        // markable — and the terminal's job is done. The PTY keeps running underneath (this
        // app's own hook owns it), so the app stays up until the tab closes.
        if (appUrl.Value is { } url)
        {
            return new AppPreviewView(plan, url);
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

    private static string[] GetCommandLine(PlanFile? plan, ReviewActionConfig? action) =>
        plan is not null && action is not null
            ? [PathHelper.GetPwshPath(), "-NoExit", "-NoProfile", "-Command", action.Command]
            : [];

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
