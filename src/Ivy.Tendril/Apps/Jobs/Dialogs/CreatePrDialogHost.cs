using Ivy.Tendril.Apps.Review.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs.Dialogs;

/// <summary>
/// Hosts <see cref="CreatePrDialog"/> outside ReviewApp (e.g. from a Jobs board card)
/// by owning the GitHub-assignees query the dialog needs, scoped to the given plan's
/// first repo. Mirrors the query in ReviewApp's ContentView.
/// </summary>
public class CreatePrDialogHost(
    IState<bool> dialogOpen,
    PlanFile plan,
    IJobService jobService,
    IConfigService config,
    Action refreshPlans) : ViewBase
{
    public override object? Build()
    {
        var githubService = UseService<IGithubService>();
        var assigneesError = UseState<string?>(null);
        var assigneesQuery = UseQuery<string[], string>(
            plan.Project,
            async (_, _) =>
            {
                var repoPath = plan.GetEffectiveRepoPaths(config).FirstOrDefault();
                if (repoPath is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var repoConfig = githubService.GetRepoConfigFromPathCached(repoPath);
                if (repoConfig is null)
                {
                    assigneesError.Set(null);
                    return Array.Empty<string>();
                }
                var (assignees, error) = await githubService.GetAssigneesAsync(repoConfig.Owner, repoConfig.Name);
                assigneesError.Set(error);
                return assignees.ToArray();
            },
            initialValue: Array.Empty<string>()
        );

        return new CreatePrDialog(dialogOpen, plan, jobService, refreshPlans, assigneesQuery, assigneesError);
    }
}
