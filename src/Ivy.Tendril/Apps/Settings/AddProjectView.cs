using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
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

        UseEffect(() =>
        {
            var raw = editName.Value ?? "";
            var sanitized = InputSanitizer.SanitizeProjectName(raw);
            if (sanitized != raw) editName.Set(sanitized);
        }, editName);

        var existingNames = config.Settings.Projects
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var nameExists = !string.IsNullOrWhiteSpace(editName.Value) && existingNames.Contains(editName.Value.Trim());
        var canCreate = !string.IsNullOrWhiteSpace(editName.Value) && !nameExists && editRepos.Value.Count > 0;

        return Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full())
            | Text.H2("Add a New Project").Bold()
            | Text.Block("A project groups one or more repositories together so Tendril can plan and verify changes across them.").Muted().Small()
            | new Separator()
            | new ProjectRepoPickerView(editRepos, editName, showBaseBranchPicker: false)
            | editName.ToTextInput("Project name (e.g. my-app)...").WithField().Label("Project Name").Required()
            | (nameExists ? new Box()
                .BorderColor(Colors.Destructive)
                .BorderRadius(BorderRadius.Rounded)
                .Content(
                    Layout.Vertical()
                    | Text.Block("A project with this name already exists.").Bold().Color(Colors.Destructive)
                    | Text.Block("To resolve this conflict, you can enter a different name above.").Small()
                ) : null!)
            | new Button("Create Project").Primary().Disabled(!canCreate || isStepLoading.Value).OnClick(() =>
            {
                if (!canCreate) return;
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
