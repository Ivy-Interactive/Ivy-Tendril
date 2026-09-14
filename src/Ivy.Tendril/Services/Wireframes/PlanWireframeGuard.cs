using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services.Wireframes;

/// <summary>
///     <see cref="WireframeLeakGuard" /> for a plan, with its project's configuration applied: each
///     repo's configured base branch, and the project's <see cref="ProjectConfig.WireframeGuard" />
///     opt-out. Every gate (execution finishing, a PR being created, a plan being completed) calls this,
///     so they all give the same answer.
/// </summary>
public static class PlanWireframeGuard
{
    public static IReadOnlyList<WireframeLeak> Check(string planFolder, IConfigService? config = null)
    {
        if (string.IsNullOrEmpty(planFolder) || !Directory.Exists(planFolder)) return [];

        ProjectConfig? project = null;
        try
        {
            config ??= new ConfigService();
            var planYaml = PlanYamlHelper.ReadPlanYaml(planFolder);
            if (!string.IsNullOrEmpty(planYaml?.Project))
                project = config.GetProject(planYaml.Project);
        }
        catch
        {
            // No readable configuration: still check, against each repo's detected default branch.
        }

        if (project is { WireframeGuard: false }) return [];

        return WireframeLeakGuard.Scan(planFolder, repoRoot => BaseBranchFor(project, repoRoot));
    }

    /// <summary>Why the plan may not move on, or null when its changes carry no wireframe code.</summary>
    public static string? BlockReason(string planFolder, IConfigService? config = null)
    {
        var leaks = Check(planFolder, config);
        return leaks.Count == 0 ? null : WireframeLeakGuard.Describe(leaks);
    }

    private static string? BaseBranchFor(ProjectConfig? project, string repoRoot)
    {
        if (project is null) return null;

        var name = Path.GetFileName(repoRoot.TrimEnd('/', '\\'));
        return project.Repos.FirstOrDefault(r =>
                Path.GetFileName(Environment.ExpandEnvironmentVariables(r.Path).TrimEnd('/', '\\'))
                    .Equals(name, StringComparison.OrdinalIgnoreCase))
            ?.BaseBranch;
    }
}
