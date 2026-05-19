using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectInputStepView(
    IState<int> stepperIndex,
    IState<int> projectSubStep,
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading) : ViewBase
{
    public override object Build()
    {
        UseEffect(() =>
        {
            var raw = projectName.Value ?? "";
            var sanitized = InputSanitizer.SanitizeProjectName(raw);
            if (sanitized != raw) projectName.Set(sanitized);
        }, projectName);

        var canContinue = selectedRepos.Value.Count > 0
                          && !string.IsNullOrWhiteSpace(projectName.Value);

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                .OnClick(() => stepperIndex.Set(stepperIndex.Value - 1))
            | new Spacer()
            | new Button("Skip").Ghost().Large()
                .OnClick(() => stepperIndex.Set(3))
            | new Button("Create Project").Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(!canContinue)
                .OnClick(() => projectSubStep.Set(1));

        return Layout.Vertical().Gap(4).Margin(0, 0, 0, 20)
               | Text.H3("Setup your first project")
               | Text.Muted("A project groups one or more repositories together so Tendril can plan and verify changes across them.")
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | new Spacer()
               | buttonArea;
    }
}
