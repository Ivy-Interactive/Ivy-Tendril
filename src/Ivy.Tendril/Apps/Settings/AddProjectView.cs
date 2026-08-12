using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class AddProjectView(
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    Action<string>? onCreated = null) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var isStepLoading = UseState(false);

        var existingNames = config.Settings.Projects
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nameError = InputSanitizer.DescribeProjectNameError(editName.Value);
        if (nameError == null && !string.IsNullOrWhiteSpace(editName.Value) && existingNames.Contains(editName.Value.Trim()))
            nameError = $"A project named '{editName.Value.Trim()}' already exists.";

        return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
            | Text.H2("Add a New Project").Bold()
            | Text.Block("Configure repository folders, agent settings, and local permissions for a new project.").Muted().Small()
            | new Separator()
            | editName.ToTextInput("Project name (e.g. my-app)...").Invalid(nameError).WithField().Label("Project Name").Required()
            | Text.Block("Repositories / Folders").Bold().Small()
            | new ProjectRepoPickerView(editRepos, showBaseBranchPicker: false)
            | new Button("Create Project").Primary().Disabled(nameError != null || string.IsNullOrWhiteSpace(editName.Value)).OnClick(() =>
            {
                if (nameError != null || string.IsNullOrWhiteSpace(editName.Value)) return;
                var newProj = new ProjectConfig
                {
                    Name = editName.Value.Trim(),
                    Repos = new List<RepoRef>(editRepos.Value)
                };
                config.Settings.Projects.Add(newProj);
                try
                {
                    config.SaveSettings();
                    refreshToken.Refresh();
                    client.Toast($"Created project '{newProj.Name}'", "Success");
                    onCreated?.Invoke(newProj.Name);
                }
                catch (Exception ex)
                {
                    client.Toast($"Failed to create project: {ex.Message}", "Error");
                }
            });
    }
}
