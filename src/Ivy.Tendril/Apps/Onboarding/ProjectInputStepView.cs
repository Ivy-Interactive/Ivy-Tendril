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
    string skipButtonText = "Skip",
    string nextButtonText = "AI Setup",
    string backButtonText = "Back",
    string title = "Setup your first project",
    bool disableSkipWhenCannotContinue = false) : ViewBase
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

        var backButton = new Button(backButtonText).Outline().Large().OnClick(onBack);
        if (backButtonText == "Back") backButton.Icon(Icons.ArrowLeft);

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | backButton
            | new Spacer()
            | (onSkip != null ? (object)new Button(skipButtonText).Ghost().Large().Disabled(disableSkipWhenCannotContinue && !canContinue).OnClick(() => onSkip()) : new Spacer())
            | new Button(nextButtonText).Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(!canContinue)
                .OnClick(onNext);

        return Layout.Vertical()
               | Text.H3(title)
               | Text.Muted("A project groups one or more repositories together so Tendril can plan and verify changes across them.")
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | new Spacer()
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (nameExists ? Text.Danger("A project with this name already exists.") : null!)
               | new Spacer()
               | buttonArea;
    }
}
