using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Hooks;

public record PreflightResult(
    List<(string RepoPath, string BaseBranch, DirtyRepoResult DirtyState)> DirtyRepos);

public static class UsePreflightCheckExtensions
{
    public static (
        Action<string, Action<PreflightResult>> RunCheck,
        bool IsChecking,
        PreflightResult? Result
    ) UsePreflightCheck(this IViewContext context)
    {
        var gitService = context.UseService<IGitService>();
        var configService = context.UseService<IConfigService>();
        var isChecking = context.UseState(false);
        var result = context.UseState((PreflightResult?)null);

        return (RunCheck, isChecking.Value, result.Value);

        void RunCheck(string projectValue, Action<PreflightResult> onComplete)
        {
            isChecking.Set(true);
            result.Set(null!);

            Task.Run(() =>
            {
                var projectNames = ProjectHelper.ParseProjects(projectValue);
                var dirtyRepos = new List<(string, string, DirtyRepoResult)>();
                var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var projectName in projectNames)
                {
                    var project = configService.GetProject(projectName);
                    if (project is null) continue;

                    foreach (var repo in project.Repos)
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(repo.Path);
                        if (!checkedPaths.Add(expanded)) continue;

                        var baseBranch = repo.BaseBranch ?? GitHelper.ResolveDefaultBranch(expanded, configService.TendrilHome);

                        var checkResult = gitService.GetRepoDirtyState(expanded, baseBranch);
                        if (checkResult.IsSuccess && checkResult.Value!.IsDirty)
                            dirtyRepos.Add((expanded, baseBranch, checkResult.Value));
                    }
                }

                var preflightResult = new PreflightResult(dirtyRepos);
                result.Set(preflightResult);
                isChecking.Set(false);
                onComplete(preflightResult);
            });
        }
    }
}
