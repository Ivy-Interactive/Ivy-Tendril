using Ivy.Hooks.Pty;
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

        // The plan/project/action lookup happens once via UseMemo (itself a hook, so this still satisfies
        // IVYHOOK005's "hooks must come first" rule - it can't be a plain statement preceding
        // UsePty). GetCommandLine then derives its argv from the already-resolved action. When
        // nothing resolves, GetCommandLine returns an empty array, which makes UsePty's StartPtyAsync
        // a no-op, so nothing is spawned.
        var (plan, project, action, workingDirectory) = UseMemo(() =>
        {
            if (!string.IsNullOrEmpty(args?.PlanId))
            {
                var resolvedPlan = ResolvePlan(planService, args.PlanId);
                var resolvedAction = resolvedPlan is not null
                    ? ResolveAction(configService, resolvedPlan.Project, args?.ActionName)
                    : null;
                // The project config comes along for its port definitions: it fixes the order the
                // plan's allocated ports are presented in, which decides the primary %PORT%.
                var planProject = resolvedPlan is not null ? configService.GetProject(resolvedPlan.Project) : null;
                return (resolvedPlan, planProject, resolvedAction, resolvedPlan?.FolderPath);
            }

            if (!string.IsNullOrEmpty(args?.ProjectName))
            {
                var resolvedProject = configService.GetProject(args.ProjectName);
                var resolvedAction = ResolveAction(configService, args.ProjectName, args?.ActionName);
                var workDir = ResolveWorkingDirectory(configService, null, resolvedProject);
                return ((PlanFile?)null, resolvedProject, resolvedAction, workDir);
            }

            return ((PlanFile?)null, (ProjectConfig?)null, (ReviewActionConfig?)null, (string?)null);
        });

        var ptyHandle = Context.UsePty(
            GetCommandLine(action, plan, project),
            workingDirectory,
            new PtyOptions
            {
                Environment = BuildEnvironment(plan, project)
            });

        if (!string.IsNullOrEmpty(args?.PlanId))
        {
            if (plan is null)
            {
                return Text.Muted("Plan not found.");
            }

            if (action is null)
            {
                return Text.Muted($"Review action \"{args?.ActionName}\" is not configured for project \"{plan.Project}\".");
            }
        }
        else if (!string.IsNullOrEmpty(args?.ProjectName))
        {
            if (project is null)
            {
                return Text.Muted($"Project \"{args.ProjectName}\" not found.");
            }

            if (action is null)
            {
                return Text.Muted($"Review action \"{args?.ActionName}\" is not configured for project \"{project.Name}\".");
            }
        }
        else
        {
            return Text.Muted("Plan or project not specified.");
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

    private static string[] GetCommandLine(ReviewActionConfig? action, PlanFile? plan, ProjectConfig? project) =>
        action is not null
            ? [PathHelper.GetPwshPath(), "-NoExit", "-NoProfile", "-Command",
                InterpolateCommand(action.Command, ResolvePorts(plan, project))]
            : [];

    /// <summary>
    ///     Substitutes the session's ports into the command: <c>${ports.&lt;name&gt;}</c> for a named
    ///     service and <c>%PORT%</c> for the primary one. pwsh does not expand <c>%VAR%</c>, so a command
    ///     written that way has to be rewritten here rather than left to the injected environment.
    /// </summary>
    internal static string InterpolateCommand(string command, IReadOnlyDictionary<string, int> ports)
    {
        if (string.IsNullOrEmpty(command) || ports.Count == 0) return command;

        var expanded = EnvironmentMaterializationHelper.ExpandPortPlaceholders(command, ports);
        return expanded.Replace("%PORT%", ports.First().Value.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The ports this session should use, in the project's configured order so the first entry is the
    ///     primary one. A plan contributes the ports allocated to its worktree; a project opened without a
    ///     plan has no worktree and therefore no allocation, so its configured defaults are used instead.
    /// </summary>
    internal static Dictionary<string, int> ResolvePorts(PlanFile? plan, ProjectConfig? project)
    {
        var allocated = plan?.AllocatedPorts ?? new Dictionary<string, int>();
        var ports = new Dictionary<string, int>(StringComparer.Ordinal);

        if (project is not null)
            foreach (var (name, config) in project.Ports)
            {
                if (allocated.TryGetValue(name, out var port))
                    ports[name] = port;
                else if (config.DefaultPort > 0)
                    ports[name] = config.DefaultPort;
            }

        // Ports the project no longer configures are still honoured while the worktree that uses them
        // exists, so a command referencing one keeps working until the plan is cleaned up.
        foreach (var (name, port) in allocated)
            ports.TryAdd(name, port);

        return ports;
    }

    /// <summary>
    ///     Environment injected into the PTY: <c>PORT_&lt;NAME&gt;</c> per named service plus <c>PORT</c>
    ///     for the primary one, and the plan's identity so a review action can locate its worktree
    ///     without the command line having to hard-code paths.
    /// </summary>
    internal static Dictionary<string, string> BuildEnvironment(PlanFile? plan, ProjectConfig? project)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var ports = ResolvePorts(plan, project);

        foreach (var (name, port) in ports)
            env[$"PORT_{ToEnvVarSuffix(name)}"] = port.ToString();

        if (ports.Count > 0)
            env["PORT"] = ports.First().Value.ToString();

        if (plan is not null)
        {
            env["PLAN_ID"] = plan.Id.ToString("D5");
            env["PLAN_FOLDER"] = plan.FolderPath;
            env["PROJECT_NAME"] = plan.Project;

            if (ResolveWorktreeDir(plan) is { } worktreeDir)
                env["WORKTREE_DIR"] = worktreeDir;
        }
        else if (project is not null)
        {
            env["PROJECT_NAME"] = project.Name;
        }

        return env;
    }

    /// <summary>Uppercases a port name into an environment variable suffix (<c>web-api</c> → <c>WEB_API</c>).</summary>
    private static string ToEnvVarSuffix(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? char.ToUpperInvariant(c) : '_'));

    /// <summary>The plan's first existing worktree, or null when none has been created.</summary>
    private static string? ResolveWorktreeDir(PlanFile plan) =>
        plan.Repos
            .Select(repo => WorktreePathHelper.GetWorktreePath(plan.FolderPath, repo))
            .FirstOrDefault(Directory.Exists);

    private static PlanFile? ResolvePlan(IPlanReaderService planService, string? planId)
    {
        if (string.IsNullOrEmpty(planId)) return null;
        var folder = Path.Combine(planService.PlansDirectory, planId);
        return planService.GetPlanByFolder(folder);
    }

    internal static string? ResolveWorkingDirectory(IConfigService configService, PlanFile? plan, ProjectConfig? project)
    {
        if (plan is not null) return plan.FolderPath;
        if (project is not null)
        {
            var firstRepo = project.Repos.FirstOrDefault()?.Path;
            if (!string.IsNullOrWhiteSpace(firstRepo))
            {
                var expanded = VariableExpansion.ExpandVariables(firstRepo, configService.TendrilHome);
                return Directory.Exists(expanded) ? expanded : (Directory.Exists(firstRepo) ? firstRepo : configService.TendrilHome);
            }
            return configService.TendrilHome;
        }
        return null;
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
