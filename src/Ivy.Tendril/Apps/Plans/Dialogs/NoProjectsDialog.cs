namespace Ivy.Tendril.Apps.Plans.Dialogs;

public class NoProjectsDialog(Action onClose, Action onGoToProjects) : ViewBase
{
    public override object Build()
    {
        return new Dialog(
            _ => onClose(),
            new DialogHeader("No Projects"),
            new DialogBody(
                Text.P("No projects are configured. Add a project to get started with creating plans.")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(onClose),
                new Button("Go to Projects").Primary().OnClick(() =>
                {
                    onGoToProjects();
                    onClose();
                })
            )
        );
    }
}
