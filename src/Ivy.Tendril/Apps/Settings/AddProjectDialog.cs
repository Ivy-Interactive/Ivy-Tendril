using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings;

public class AddProjectDialog(
    IState<bool> isOpen,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken,
    Action<string>? onCreated = null) : ViewBase
{
    public override object? Build()
    {
        var projectName = UseState("");
        var repos = UseState(new List<RepoRef>());
        var isCreating = UseState(false);

        UseEffect(() =>
        {
            var raw = projectName.Value ?? "";
            var sanitized = InputSanitizer.SanitizeProjectName(raw);
            if (sanitized != raw) projectName.Set(sanitized);
        }, projectName);

        if (!isOpen.Value) return null;

        var existingProject = !string.IsNullOrWhiteSpace(projectName.Value)
            ? config.Settings.Projects.FirstOrDefault(p => p.Name.Equals(projectName.Value.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;
        var nameExists = existingProject != null;
        var canCreate = !string.IsNullOrWhiteSpace(projectName.Value) && !nameExists && repos.Value.Count > 0;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Add New Project"),
            new DialogBody(
                Layout.Vertical()
                | Text.Block("A project groups one or more repositories together so Tendril can plan and verify changes across them.").Muted().Small()
                | new ProjectRepoPickerView(repos, projectName, showBaseBranchPicker: false)
                | projectName.ToTextInput("Project name...").WithField().Label("Project Name").Required()
                | (nameExists ? new Box()
                    .BorderColor(Colors.Destructive)
                    .BorderRadius(BorderRadius.Rounded)
                    .Content(
                        Layout.Vertical()
                        | Text.Block("A project with this name already exists.").Bold().Color(Colors.Destructive)
                        | Text.Block("To resolve this conflict, you can enter a different name above.").Small()
                    ) : null!)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Create Project").Primary().Disabled(!canCreate || isCreating.Value).Loading(isCreating.Value).OnClick(() =>
                {
                    var projName = InputSanitizer.SanitizeProjectName(projectName.Value.Trim());
                    if (string.IsNullOrWhiteSpace(projName)) return;

                    isCreating.Set(true);
                    try
                    {
                        var newProj = new ProjectConfig
                        {
                            Name = projName,
                            Repos = new List<RepoRef>(repos.Value)
                        };
                        config.Settings.Projects.Add(newProj);
                        config.SaveSettings();
                        isOpen.Set(false);
                        refreshToken.Refresh();
                        client.Toast($"Created project '{projName}'", "Project Created");
                        onCreated?.Invoke(projName);
                    }
                    catch (Exception ex)
                    {
                        client.Toast($"Failed to create project: {ex.Message}", "Error");
                    }
                    finally
                    {
                        isCreating.Set(false);
                    }
                })
            )
        ).Width(Size.Rem(38));
    }
}
