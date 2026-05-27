using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectInputStepView(
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading,
    Action onNext,
    Action? onBack = null,
    Action? onSkip = null,
    string skipButtonText = "Skip",
    string nextButtonText = "Create Project",
    string title = "Setup your first project",
    bool disableSkipWhenCannotContinue = false,
    bool showHeader = true) : ViewBase
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

        var existingProject = !string.IsNullOrWhiteSpace(projectName.Value)
            ? config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projectName.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;
        var nameExists = existingProject != null;

        void UseExisting()
        {
            if (existingProject != null)
            {
                selectedRepos.Set(new List<RepoRef>(existingProject.Repos));
                onNext();
            }
        }

        var canContinue = selectedRepos.Value.Count > 0
                          && !string.IsNullOrWhiteSpace(projectName.Value)
                          && !nameExists;

        var buttonArea = Layout.Horizontal().Width(Size.Full())
            | (onBack != null ? (object)new Button("Back").Outline().Large().Icon(Icons.ArrowLeft).OnClick(onBack) : new Spacer())
            | new Spacer()
            | (onSkip != null ? (object)new Button(skipButtonText).Ghost().Large().Disabled(disableSkipWhenCannotContinue && !canContinue).OnClick(() => onSkip()) : new Spacer())
            | new Button(nextButtonText).Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(!canContinue)
                .OnClick(onNext);

        return Layout.Vertical()
               | (showHeader ? Text.H3(title) : null!)
               | Text.Muted("A project groups one or more repositories together so Tendril can plan and verify changes across them.")
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (nameExists ? new Box()
                   .Background(Colors.Destructive)
                   .Padding(8)
                   .BorderRadius(BorderRadius.Rounded)
                   .Content(
                       Layout.Vertical().Gap(2)
                       | Text.Block("A project with this name already exists.").Bold().Color(Colors.White)
                       | Text.Block("To resolve this conflict, you can either enter a different name above, or proceed using the existing project's configuration (its repository path and settings will be preserved).").Color(Colors.White).Small()
                       | (Layout.Horizontal().Margin(0, 0, 4, 0)
                           | new Button("Use Existing Project Configuration")
                               .Outline()
                               .Small()
                               .OnClick(UseExisting)
                         )
                   )
                   : null!)
               | new Spacer()
               | buttonArea;
    }
}
