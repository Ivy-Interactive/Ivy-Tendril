using Ivy.Core.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using Ivy.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril.Apps.Setup.Dialogs;

public class EditProjectDialog(
    IState<int?> editIndex,
    List<ProjectConfig> projects,
    List<string> allVerifications,
    IConfigService config,
    IClientProvider client,
    RefreshToken refreshToken) : ViewBase
{
    private readonly IState<int?> _editIndex = editIndex;
    private readonly List<ProjectConfig> _projects = projects;
    private readonly List<string> _allVerifications = allVerifications;
    private readonly IConfigService _config = config;
    private readonly IClientProvider _client = client;
    private readonly RefreshToken _refreshToken = refreshToken;

    public override object? Build()
    {
        var editName = UseState("");
        var editColor = UseState<Colors?>(null);
        var showColorPicker = UseState(false);
        var editContext = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var editVerifications = UseState(new List<ProjectVerificationRef>());
        var newRepoPath = UseState<string?>(null);
        var newRepoPrRule = UseState("default");
        var newRepoBaseBranch = UseState("");
        var repoPathError = UseState<string?>(null);
        var editingRepoIndex = UseState<int?>(-1);
        var editingRepoPath = UseState<string?>(null);
        var editingRepoBaseBranch = UseState("");
        var editingRepoError = UseState<string?>(null);

        var (folderDialogView, showFolderDialog, selectedFolderPath) = UseFolderDialog();

        UseEffect(() =>
        {
            if (_editIndex.Value == null)
            {
                editName.Set("");
                editColor.Set(null);
                editContext.Set("");
                editRepos.Set(new List<RepoRef>());
                editVerifications.Set(new List<ProjectVerificationRef>());
            }
            else if (_editIndex.Value >= 0)
            {
                var project = _projects[_editIndex.Value.Value];
                editName.Set(project.Name);
                editColor.Set(Enum.TryParse<Colors>(project.Color, out var c) ? c : null);
                editContext.Set(project.Context);
                editRepos.Set(
                    new List<RepoRef>(project.Repos.Select(r => new RepoRef { Path = r.Path, PrRule = r.PrRule, BaseBranch = r.BaseBranch, SyncStrategy = r.SyncStrategy })));
                editVerifications.Set(new List<ProjectVerificationRef>(
                    project.Verifications.Select(v => new ProjectVerificationRef
                    { Name = v.Name, Required = v.Required })));
            }

            newRepoPath.Set(null);
            newRepoPrRule.Set("default");
            newRepoBaseBranch.Set("");
            repoPathError.Set(null);
            editingRepoIndex.Set(-1);
            editingRepoPath.Set(null);
            editingRepoBaseBranch.Set("");
            editingRepoError.Set(null);
        }, _editIndex);

        UseEffect(() =>
        {
            if (selectedFolderPath.Value != null)
                newRepoPath.Set(selectedFolderPath.Value);
        }, selectedFolderPath);

        if (_editIndex.Value == -1) return null;

        Context.TryUseService<DesktopWindow>(out var desktop);

        var isNew = _editIndex.Value == null;

        var reposLayout = Layout.Vertical().Gap(2);
        var currentRepos = editRepos.Value;
        for (var i = 0; i < currentRepos.Count; i++)
        {
            var ri = i;
            var repo = currentRepos[ri];
            var expandedPath = VariableExpansion.ExpandVariables(repo.Path, _config.TendrilHome);
            var pathExists = Directory.Exists(expandedPath);
            var isGitRepo = pathExists && Path.Exists(Path.Combine(expandedPath, ".git"));
            var isEditing = editingRepoIndex.Value == ri;

            if (isEditing)
            {
                reposLayout |= Layout.Vertical().Gap(1)
                               | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                               | (!isGitRepo
                                   ? (object)new Icon(Icons.TriangleAlert, Colors.Warning).Small()
                                       .WithTooltip(!pathExists
                                           ? $"Path does not exist: {expandedPath}"
                                           : $"Not a git repository: {expandedPath}")
                                   : null!)
                               | editingRepoPath
                                   .ToTextInput("Your repository folder")
                                   .Width(Size.Grow())
                               | new Badge(repo.PrRule).Variant(BadgeVariant.Outline)
                               | new Button().Icon(Icons.Check).Ghost().Small().OnClick(() =>
                               {
                                   var newPath = editingRepoPath.Value;
                                   if (string.IsNullOrWhiteSpace(newPath))
                                   {
                                       editingRepoError.Set("Path cannot be empty");
                                       return;
                                   }

                                   var expandedNewPath = VariableExpansion.ExpandVariables(newPath, _config.TendrilHome);
                                   if (!Directory.Exists(expandedNewPath))
                                   {
                                       editingRepoError.Set($"Directory does not exist: {expandedNewPath}");
                                       return;
                                   }

                                   if (!Path.Exists(Path.Combine(expandedNewPath, ".git")))
                                   {
                                       editingRepoError.Set($"Directory is not a git repository: {expandedNewPath}");
                                       return;
                                   }

                                   var trimmedBranch = editingRepoBaseBranch.Value?.Trim();
                                   var list = new List<RepoRef>(editRepos.Value);
                                   list[ri] = new RepoRef
                                   {
                                       Path = newPath,
                                       PrRule = repo.PrRule,
                                       BaseBranch = string.IsNullOrEmpty(trimmedBranch) ? null : trimmedBranch,
                                       SyncStrategy = repo.SyncStrategy,
                                   };
                                   editRepos.Set(list);
                                   editingRepoIndex.Set(-1);
                                   editingRepoError.Set(null);
                               })
                               | new Button().Icon(Icons.X).Ghost().Small().OnClick(() =>
                               {
                                   editingRepoIndex.Set(-1);
                                   editingRepoError.Set(null);
                               }))
                               | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                               | editingRepoBaseBranch
                                   .ToTextInput("Base branch (optional)")
                                   .Width(Size.Grow()));
            }
            else
            {
                var pathText = Text.Block(repo.Path);
                if (!isGitRepo) pathText = pathText.Color(Colors.Red);

                reposLayout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                               | (!isGitRepo
                                   ? (object)new Icon(Icons.TriangleAlert, Colors.Warning).Small()
                                       .WithTooltip(!pathExists
                                           ? $"Path does not exist: {expandedPath}"
                                           : $"Not a git repository: {expandedPath}")
                                   : null!)
                               | pathText
                               | new Badge(repo.PrRule).Variant(BadgeVariant.Outline)
                               | (!string.IsNullOrEmpty(repo.BaseBranch)
                                   ? (object)new Badge(repo.BaseBranch).Variant(BadgeVariant.Secondary)
                                   : null!)
                               | new Spacer().Width(Size.Grow())
                               | new Button().Icon(Icons.Pencil).Ghost().Small()
                                   .OnClick(() =>
                                   {
                                       editingRepoIndex.Set(ri);
                                       editingRepoPath.Set(repo.Path);
                                       editingRepoBaseBranch.Set(repo.BaseBranch ?? "");
                                       editingRepoError.Set(null);
                                   })
                                   .WithTooltip("Edit repository")
                               | new Button().Icon(Icons.Trash).Ghost().Small().OnClick(() =>
                               {
                                   var list = new List<RepoRef>(editRepos.Value);
                                   list.RemoveAt(ri);
                                   editRepos.Set(list);
                               });
            }
        }

        if (editingRepoError.Value != null) reposLayout |= Text.Danger(editingRepoError.Value);

        if (repoPathError.Value != null) reposLayout |= Text.Danger(repoPathError.Value);

        Action addRepoAction = () =>
        {
            if (!string.IsNullOrWhiteSpace(newRepoPath.Value))
            {
                var expandedNewPath = VariableExpansion.ExpandVariables(newRepoPath.Value, _config.TendrilHome);

                if (!Directory.Exists(expandedNewPath))
                {
                    repoPathError.Set($"Directory does not exist: {expandedNewPath}");
                    return;
                }

                if (!Path.Exists(Path.Combine(expandedNewPath, ".git")))
                {
                    repoPathError.Set($"Directory is not a git repository: {expandedNewPath}");
                    return;
                }

                var trimmedBaseBranch = newRepoBaseBranch.Value?.Trim();
                var list = new List<RepoRef>(editRepos.Value)
                {
                    new()
                    {
                        Path = newRepoPath.Value,
                        PrRule = newRepoPrRule.Value,
                        BaseBranch = string.IsNullOrEmpty(trimmedBaseBranch) ? null : trimmedBaseBranch,
                    }
                };
                editRepos.Set(list);
                newRepoPath.Set(null);
                newRepoPrRule.Set("default");
                newRepoBaseBranch.Set("");
                repoPathError.Set(null);
            }
        };

        if (desktop != null)
        {
            reposLayout |= Layout.Vertical().Gap(1)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                           | new Button(newRepoPath.Value ?? "Your repository folder")
                               .Outline()
                               .Width(Size.Percent(100))
                               .OnClick(async () =>
                               {
                                   var tcs = new TaskCompletionSource<string[]?>();
                                       var thread = new Thread(() =>
                                       {
                                           try
                                           {
                                               var folders = desktop.ShowSelectFolderDialog("Select Repository Folder");
                                               tcs.SetResult(folders);
                                           }
                                           catch (Exception ex)
                                           {
                                               tcs.SetException(ex);
                                           }
                                       });
                                       
                                       if (OperatingSystem.IsWindows())
                                       {
                                           thread.SetApartmentState(ApartmentState.STA);
                                       }
                                       thread.Start();

                                       var result = await tcs.Task;
                                       if (result != null && result.Length > 0)
                                       {
                                           newRepoPath.Set(result[0]);
                                       }
                               })
                           | newRepoPrRule.ToSelectInput(new List<string> { "default", "yolo" })
                               .Width(Size.Units(20)))
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                           | newRepoBaseBranch.ToTextInput("Base branch (optional)")
                               .Width(Size.Grow()));
        }
        else
        {
            reposLayout |= Layout.Vertical().Gap(1)
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                           | newRepoPath.ToTextInput("Your repository folder")
                               .Suffix(newRepoPrRule.ToSelectInput(new List<string> { "default", "yolo" }).Ghost())
                               .Width(Size.Grow()))
                           | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                           | newRepoBaseBranch.ToTextInput("Base branch (optional)")
                               .Width(Size.Grow()));
        }

        // Verifications switches
        var verificationsLayout = Layout.Vertical().Gap(1);
        foreach (var vName in _allVerifications)
        {
            var capturedName = vName;

            var enabledState = new ConvertedState<List<ProjectVerificationRef>, bool>(
                editVerifications,
                list => list.Any(v => v.Name == capturedName),
                enabled =>
                {
                    var list = new List<ProjectVerificationRef>(editVerifications.Value);
                    if (enabled)
                        list.Add(new ProjectVerificationRef { Name = capturedName, Required = false });
                    else
                        list.RemoveAll(v => v.Name == capturedName);
                    return list;
                }
            );

            var requiredState = new ConvertedState<List<ProjectVerificationRef>, bool>(
                editVerifications,
                list => list.FirstOrDefault(v => v.Name == capturedName)?.Required ?? false,
                required =>
                {
                    var list = new List<ProjectVerificationRef>(editVerifications.Value);
                    var item = list.FirstOrDefault(v => v.Name == capturedName);
                    if (item != null) item.Required = required;
                    return list;
                }
            );

            var isEnabled = enabledState.Value;

            verificationsLayout |= Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                                   | enabledState.ToSwitchInput(label: capturedName)
                                   | new Spacer().Width(Size.Grow())
                                   | (isEnabled
                                       ? (object)requiredState.ToBoolInput("Required")
                                       : new Spacer());
        }

        return new Dialog(
            _ =>
            {
                _editIndex.Set(-1);
                editingRepoIndex.Set(-1);
                editingRepoError.Set(null);
            },
            new DialogHeader(isNew ? "Add Project" : $"Edit Project: {editName.Value}"),
            new DialogBody(
                Layout.Vertical().Gap(4)
                | editName.ToTextInput("Project name...").WithField().Label("Name")
                | (Layout.Vertical().Gap(1)
                   | Text.Block("Color").Small()
                   | (Layout.Horizontal().Gap(2).AlignContent(Align.TopLeft)
                      | new Button(editColor.Value?.ToString() ?? "Select Color").Outline()
                          .OnClick(() => showColorPicker.Set(true))
                      | (editColor.Value != null
                          ? (object)new Box().Background(editColor.Value.Value).Width(Size.Units(8)).Height(Size.Units(8))
                          : null!)))
                | editColor.ToColorInput().ToDialog(showColorPicker, title: "Select Color")
                | editContext.ToTextareaInput("Project context or prompt for AI agents (optional)...").Rows(4)
                    .WithField().Label("Context / Prompt (Optional)")
                | (Layout.Vertical().Gap(2)
                   | Text.Block("Repositories").Bold()
                   | reposLayout)
                | (Layout.Vertical().Gap(2)
                   | Text.Block("Verifications").Bold()
                   | verificationsLayout)
                | folderDialogView
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() =>
                {
                    _editIndex.Set(-1);
                    editingRepoIndex.Set(-1);
                    editingRepoError.Set(null);
                }),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    addRepoAction();
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    var project = isNew ? new ProjectConfig() : _projects[_editIndex.Value!.Value];
                    project.Name = editName.Value;
                    project.Color = editColor.Value?.ToString() ?? "";
                    project.Context = editContext.Value;
                    project.Repos = new List<RepoRef>(editRepos.Value);
                    project.Verifications = new List<ProjectVerificationRef>(editVerifications.Value);
                    if (isNew) _projects.Add(project);
                    _config.SaveSettings();
                    _editIndex.Set(-1);
                    editingRepoIndex.Set(-1);
                    editingRepoError.Set(null);
                    _refreshToken.Refresh();
                    _client.Toast($"Project '{editName.Value}' saved", "Saved");
                })
            )
        ).Width(Size.Rem(40));
    }
}
