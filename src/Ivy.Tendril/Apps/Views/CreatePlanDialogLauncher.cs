using Ivy.Tendril.Apps.Drafts.Dialogs;
using Ivy.Tendril.Apps.Views.Dialogs;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Views;

public class CreatePlanDialogLauncher(Func<Action, object> renderTrigger) : ViewBase
{
    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var navigator = UseNavigation();
        var lastSelectedProjects = UseState<string[]>(["Auto"]);
        var showDirtyDialog = UseState(false);
        var pendingJobArgs = UseState<CreatePlanArgs?>(null);
        var (runPreflight, _, preflightResult) = Context.UsePreflightCheck();

        var (dialog, showDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            var projectNames = configService.Projects.Select(p => p.Name).ToList();
            if (projectNames.Count == 0)
                return new NoProjectsDialog(
                    () => isOpen.Set(false),
                    () => navigator.Navigate<SettingsApp>()
                );
            return new CreatePlanDialog(
                projectNames,
                (description, projects, priority) =>
                {
                    lastSelectedProjects.Set(projects);
                    var project = string.Join(",", projects);
                    var args = new CreatePlanArgs(description, project, priority, Force: true);
                    pendingJobArgs.Set(args);
                    isOpen.Set(false);
                    runPreflight(project, result =>
                    {
                        if (result.DirtyRepos.Count > 0)
                            showDirtyDialog.Set(true);
                        else
                            LaunchCreatePlan(args);
                    });
                },
                () => isOpen.Set(false),
                lastSelectedProjects.Value
            );
        });

        var dirtyRepoDialog = showDirtyDialog.Value && preflightResult is { DirtyRepos.Count: > 0 } && pendingJobArgs.Value is not null
            ? new DirtyRepoDialog(
                showDirtyDialog,
                preflightResult,
                proceedLabel: "Create Anyway",
                contextMessage: "The plan will be based on this state, but ExecutePlan will branch from origin/<baseBranch>. Commit and push first if these changes should be included in the plan.",
                onSyncRepos: () => LaunchWithSync(pendingJobArgs.Value, preflightResult),
                onProceed: () => LaunchCreatePlan(pendingJobArgs.Value))
            : null;

        var elements = new List<object> { renderTrigger(() => showDialog()), dialog };
        if (dirtyRepoDialog is not null)
            elements.Add(dirtyRepoDialog);

        return new Fragment(elements.ToArray());

        void LaunchCreatePlan(CreatePlanArgs args)
        {
            jobService.StartJob(args);
            pendingJobArgs.Set(null);
        }

        void LaunchWithSync(CreatePlanArgs args, PreflightResult preflight)
        {
            var syncJobIds = new List<string>();
            foreach (var (repoPath, baseBranch, _) in preflight.DirtyRepos)
            {
                var jobId = jobService.StartJob(new SyncRepoArgs(repoPath, baseBranch));
                syncJobIds.Add(jobId);
            }

            jobService.StartJob(args with { WaitForJobs = syncJobIds });
            pendingJobArgs.Set(null);
        }
    }
}
