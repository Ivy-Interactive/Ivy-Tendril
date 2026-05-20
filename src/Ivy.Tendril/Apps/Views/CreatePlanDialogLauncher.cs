using Ivy.Tendril.Apps.Plans.Dialogs;
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
                    jobService.StartJob(new CreatePlanArgs(description, project, priority, Force: true));
                },
                () => isOpen.Set(false),
                lastSelectedProjects.Value
            );
        });

        return new Fragment(renderTrigger(() => showDialog()), dialog);
    }
}
