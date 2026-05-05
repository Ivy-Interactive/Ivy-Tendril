using Ivy.Tendril.Apps;
using Ivy.Tendril.Apps.Plans.Dialogs;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Views;

public class CreatePlanDialogLauncher(Func<Action, object> renderTrigger) : ViewBase
{
    public override object Build()
    {
        var jobService = UseService<IJobService>();
        var configService = UseService<IConfigService>();
        var navigator = UseService<INavigator>();
        var dialogOpen = UseState(false);
        var lastSelectedProjects = UseState<string[]>(["Auto"]);

        var projectNames = configService.Projects.Select(p => p.Name).ToList();

        var elements = new List<object>
        {
            renderTrigger(() => dialogOpen.Set(true))
        };

        if (dialogOpen.Value)
        {
            if (projectNames.Count == 0)
                elements.Add(new NoProjectsDialog(
                    () => dialogOpen.Set(false),
                    () => navigator.Navigate<SetupApp>()
                ));
            else
                elements.Add(new CreatePlanDialog(
                    projectNames,
                    (description, projects, priority) =>
                    {
                        lastSelectedProjects.Set(projects);
                        var project = string.Join(",", projects);
                        jobService.StartJob(Constants.JobTypes.CreatePlan, "-Description", $"{description} [FORCE]", "-Project", project, "-Priority", priority.ToString());
                    },
                    () => dialogOpen.Set(false),
                    lastSelectedProjects.Value
                ));
        }

        return new Fragment(elements.ToArray());
    }
}
