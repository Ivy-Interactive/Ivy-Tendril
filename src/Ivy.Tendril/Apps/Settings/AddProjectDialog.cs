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

        var sanitized = InputSanitizer.SanitizeProjectName(projectName.Value ?? "");
        var nameExists = !string.IsNullOrWhiteSpace(sanitized) &&
                         config.Settings.Projects.Any(p => p.Name.Equals(sanitized, StringComparison.OrdinalIgnoreCase));
        var canCreate = !string.IsNullOrWhiteSpace(sanitized) && !nameExists && repos.Value.Count > 0;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Add New Project"),
            new DialogBody(
                Layout.Vertical()
                | projectName.ToTextInput("Project name...").WithField().Label("Project Name").Required()
                | (nameExists ? Text.Block("A project with this name already exists.").Color(Colors.Destructive).Small() : null!)
                | Text.Block("Repositories").Bold().Small()
                | new ProjectRepoPickerView(repos, showBaseBranchPicker: false)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Create Project").Primary().Disabled(!canCreate || isCreating.Value).OnClick(() =>
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
        ).Width(Size.Rem(35));
    }
}
