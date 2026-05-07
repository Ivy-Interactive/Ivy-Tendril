using Ivy.Core.Hooks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;

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
        }, _editIndex);

        if (_editIndex.Value == -1) return null;

        var isNew = _editIndex.Value == null;

        Func<RepoRef, Task<RepoRef?>> cloneRemoteOnAdd = async draft =>
        {
            if (!LooksLikeUrl(draft.Path)) return draft;

            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
            var reposDir = Path.Combine(tendrilHome, "Repos");
            Directory.CreateDirectory(reposDir);

            var repoName = ExtractRepoName(draft.Path);
            var destPath = Path.Combine(reposDir, repoName);

            var success = await GitHubCliHelper.CloneRepositoryAsync(draft.Path, destPath);
            if (!success)
            {
                _client.Toast($"Failed to fetch repository: {draft.Path}", "Error");
                return null;
            }

            return draft with { Path = destPath };
        };

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
            _ => _editIndex.Set(-1),
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
                   | new ProjectRepoPickerView(editRepos, onAdd: cloneRemoteOnAdd, showPrRule: true))
                | (Layout.Vertical().Gap(2)
                   | Text.Block("Verifications").Bold()
                   | verificationsLayout)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => _editIndex.Set(-1)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
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
                    _refreshToken.Refresh();
                    _client.Toast($"Project '{editName.Value}' saved", "Saved");
                })
            )
        ).Width(Size.Rem(40));
    }

    private static bool LooksLikeUrl(string path)
        => !string.IsNullOrEmpty(path)
           && (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("git@", StringComparison.OrdinalIgnoreCase));

    private static string ExtractRepoName(string url)
    {
        var trimmed = url;
        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];
        var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : Guid.NewGuid().ToString();
    }
}
