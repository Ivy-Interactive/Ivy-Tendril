using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectInputStepView(
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading,
    Action onBack,
    Action onNext,
    Action? onSkip = null,
    string skipButtonText = "Skip Setup") : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();

        UseEffect(() =>
        {
            var raw = projectName.Value ?? "";
            var sanitized = InputSanitizer.SanitizeProjectName(raw);
            if (sanitized != raw) projectName.Set(sanitized);
        }, projectName);

        var nameExists = !string.IsNullOrWhiteSpace(projectName.Value) &&
                         config.Settings.Projects.Any(p => p.Name.Equals(projectName.Value.Trim(), StringComparison.OrdinalIgnoreCase));

        var canContinue = selectedRepos.Value.Count > 0
                          && !string.IsNullOrWhiteSpace(projectName.Value)
                          && !nameExists;

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | new Button("Back").Outline().Large().Icon(Icons.ArrowLeft)
                .OnClick(onBack)
            | new Spacer()
            | (onSkip != null ? (object)new Button(skipButtonText).Ghost().Large().OnClick(() => onSkip()) : new Spacer())
            | new Button("Create Project").Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(!canContinue)
                .OnClick(onNext);

        return Layout.Vertical().Margin(0, 0, 0, 20)
               | Text.H3("Setup your first project")
               | Text.Muted("A project groups one or more repositories together so Tendril can plan and verify changes across them.")
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | new Spacer()
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (nameExists ? Text.Danger("A project with this name already exists.") : null!)
               | new Spacer()
               | buttonArea;
    }
}
