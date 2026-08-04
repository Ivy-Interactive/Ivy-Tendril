using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Apps.Views;

namespace Ivy.Tendril.Apps.Onboarding;

public class ProjectInputStepView(
    IState<List<RepoRef>> selectedRepos,
    IState<string> projectName,
    IState<bool> isStepLoading,
    Action onNext,
    Action? onBack = null,
    Action? onSkip = null,
    Action? onBgJob = null,
    string skipButtonText = "Skip",
    string nextButtonText = "Create Project",
    string title = "Setup your first project",
    bool disableSkipWhenCannotContinue = false,
    bool showHeader = true) : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var tendrilArgs = UseService<TendrilArgs>();
        var jobService = UseService<IJobService>();
        var client = UseService<IClientProvider>();

        var isBeta = (tendrilArgs?.Beta ?? false) ||
                     (config?.Settings?.Beta ?? false) ||
                     Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1" ||
                     Environment.GetEnvironmentVariable("IVY_BETA") == "1";

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

        Action handleBgJob = onBgJob ?? (() =>
        {
            if (!canContinue) return;
            var newProj = new ProjectConfig
            {
                Name = projectName.Value.Trim(),
                Repos = new List<RepoRef>(selectedRepos.Value)
            };
            config.Settings.Projects.Add(newProj);
            try { config.SaveSettings(); } catch { }

            jobService?.StartJob(new AddProjectArgs(newProj.Name, newProj.Repos));
            if (client != null)
                client.Toast($"Created background job for project '{newProj.Name}'", "Job Started");
            onNext();
        });

        var buttonArea = Layout.Horizontal().Width(Size.Full()).Gap(2)
            | (onSkip != null ? (object)new Button(skipButtonText).Ghost().Large().Disabled(disableSkipWhenCannotContinue && !canContinue).OnClick(() => onSkip()) : new Spacer())
            | new Spacer()
            | (onBack != null ? (object)new Button("Back").Outline().Large().Icon(Icons.ArrowLeft).OnClick(onBack) : null!)
            | (isBeta
                ? (object)new Button("Background (beta)").Outline().Large().Disabled(!canContinue).OnClick(handleBgJob)
                : null!)
            | new Button(nextButtonText).Secondary().Large().Icon(Icons.ArrowRight, Align.Right)
                .Disabled(!canContinue)
                .OnClick(onNext);

        return Layout.Vertical().Margin(0, 0, 0, 2)
               | (showHeader ? Text.H3(title) : null!)
               | Text.Muted("A project groups one or more repositories together so Tendril can plan and verify changes across them.")
               | new ProjectRepoPickerView(selectedRepos, projectName)
               | projectName.ToTextInput().WithField().Required().Label("Project Name")
               | (nameExists ? new Box()
                   .BorderColor(Colors.Destructive)
                   .Padding(8)
                   .BorderRadius(BorderRadius.Rounded)
                   .Content(
                       Layout.Vertical().Gap(2)
                       | Text.Block("A project with this name already exists.").Bold().Color(Colors.Destructive)
                       | Text.Block("To resolve this conflict, you can either enter a different name above, or proceed using the existing project's configuration (its repository path and settings will be preserved).").Small()
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
