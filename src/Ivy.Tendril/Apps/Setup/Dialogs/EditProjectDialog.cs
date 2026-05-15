using System.Diagnostics;
using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Onboarding;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Views;
using Ivy.Widgets.AgentOutputView;

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

        var editContext = UseState("");
        var editRepos = UseState(new List<RepoRef>());
        var editVerifications = UseState(new List<ProjectVerificationRef>());

        var runner = UseService<IPromptwareRunner>();
        var generateStream = UseStream<string>();
        var showGenerateDialog = UseState(false);
        var generateHandle = UseState<PromptwareRunHandle?>(null);
        var isGenerating = UseState(false);
        var generateCancelled = UseState(false);

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
            var kind = RepoPathValidator.Classify(draft.Path);
            if (kind == RepoPathKind.LocalPath || kind == RepoPathKind.Invalid) return draft;

            var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
                              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
            var reposDir = Path.Combine(tendrilHome, "Repos");
            Directory.CreateDirectory(reposDir);

            var repoName = RepoPathValidator.ExtractRepoName(draft.Path) ?? Guid.NewGuid().ToString();
            var destPath = Path.Combine(reposDir, repoName);

            var success = await CloneRepositoryAsync(draft.Path, destPath);
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

        var mainDialog = new Dialog(
            _ => _editIndex.Set(-1),
            new DialogHeader(isNew ? "Add Project" : $"Edit Project: {editName.Value}"),
            new DialogBody(
                Layout.Vertical().Gap(4)
                | editName.ToTextInput("Project name...").WithField().Label("Name")
                | editColor.ToColorInput().Variant(ColorInputVariant.SwatchPicker).Nullable().WithField().Label("Color")
                | editContext.ToTextareaInput("Project context or prompt for AI agents (optional)...").Rows(4)
                    .WithField().Label("Context / Prompt (Optional)")
                | (Layout.Vertical().Gap(2)
                   | Text.Block("Repositories").Bold()
                   | new ProjectRepoPickerView(editRepos, onAdd: cloneRemoteOnAdd, showPrRule: true))
                | (Layout.Vertical().Gap(2)
                   | Text.Block("Verifications").Bold()
                   | verificationsLayout
                   | new Button("Generate Verifications").Secondary().Icon(Icons.Sparkles)
                       .Disabled(isGenerating.Value || string.IsNullOrWhiteSpace(editName.Value))
                       .OnClick(() =>
                       {
                           generateCancelled.Set(false);
                           showGenerateDialog.Set(true);
                           isGenerating.Set(true);

                           // Save current project state to config so UpdateProject can find it
                           var project = isNew ? new ProjectConfig() : _projects[_editIndex.Value!.Value];
                           project.Name = editName.Value;
                           project.Color = editColor.Value?.ToString() ?? "";
                           project.Context = editContext.Value;
                           project.Repos = new List<RepoRef>(editRepos.Value);
                           project.Verifications = new List<ProjectVerificationRef>(editVerifications.Value);
                           if (isNew) _projects.Add(project);
                           try
                           {
                               _config.SaveSettings();
                           }
                           catch
                           {
                               if (isNew) _projects.Remove(project);
                               isGenerating.Set(false);
                               showGenerateDialog.Set(false);
                               _client.Toast("Failed to save project before generating verifications", "Error");
                               return;
                           }

                           var notifyingStream = new NotifyingStream<string>(generateStream, () => { });

                           var handle = runner.Run(new PromptwareRunOptions
                           {
                               Promptware = "UpdateProject",
                               Values = new()
                               {
                                   ["ProjectName"] = editName.Value,
                                   ["Instructions"] = "Setup verifications"
                               }
                           }, notifyingStream);

                           generateHandle.Set(handle);

                           // Run detached so the click returns immediately and the dialog
                           // stays responsive — cancelling the handle resolves Completion.
                           _ = Task.Run(async () =>
                           {
                               try
                               {
                                   await handle.Completion;
                               }
                               catch (OperationCanceledException) { }
                               catch (Exception ex)
                               {
                                   if (!generateCancelled.Value)
                                       _client.Toast($"Verification generation failed: {ex.Message}", "Error");
                               }
                               finally
                               {
                                   if (!generateCancelled.Value)
                                   {
                                       _config.ReloadSettings();

                                       var updatedProject = _config.Settings.Projects
                                           .FirstOrDefault(p => p.Name == editName.Value);
                                       if (updatedProject != null)
                                       {
                                           var merged = new List<ProjectVerificationRef>(editVerifications.Value);
                                           foreach (var v in updatedProject.Verifications)
                                           {
                                               if (!merged.Any(m => m.Name == v.Name))
                                                   merged.Add(new ProjectVerificationRef { Name = v.Name, Required = true });
                                           }
                                           editVerifications.Set(merged);

                                           // Add any new verification names to the rendered list
                                           foreach (var vc in _config.Settings.Verifications)
                                           {
                                               if (!_allVerifications.Contains(vc.Name))
                                                   _allVerifications.Add(vc.Name);
                                           }
                                       }
                                   }

                                   generateHandle.Set(null);
                                   isGenerating.Set(false);
                               }
                           });
                       }))
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => _editIndex.Set(-1)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    var project = isNew ? new ProjectConfig() : _projects[_editIndex.Value!.Value];
                    var oldName = project.Name;
                    var oldColor = project.Color;
                    var oldContext = project.Context;
                    var oldRepos = project.Repos;
                    var oldVerifications = project.Verifications;
                    project.Name = editName.Value;
                    project.Color = editColor.Value?.ToString() ?? "";
                    project.Context = editContext.Value;
                    project.Repos = new List<RepoRef>(editRepos.Value);
                    project.Verifications = new List<ProjectVerificationRef>(editVerifications.Value);
                    if (isNew) _projects.Add(project);
                    try
                    {
                        _config.SaveSettings();
                        _editIndex.Set(-1);
                        _refreshToken.Refresh();
                        _client.Toast($"Project '{editName.Value}' saved", "Saved");
                    }
                    catch (Exception ex)
                    {
                        if (isNew)
                            _projects.Remove(project);
                        else
                        {
                            project.Name = oldName;
                            project.Color = oldColor;
                            project.Context = oldContext;
                            project.Repos = oldRepos;
                            project.Verifications = oldVerifications;
                        }
                        _refreshToken.Refresh();
                        _client.Toast($"Failed to save project: {ex.Message}", "Error");
                    }
                })
            )
        ).Width(Size.Rem(40));

        void CancelGenerate()
        {
            if (isGenerating.Value)
            {
                generateCancelled.Set(true);
                generateHandle.Value?.Cancel();
                generateHandle.Set(null);
                isGenerating.Set(false);
            }
            showGenerateDialog.Set(false);
        }

        object? generateDialog = showGenerateDialog.Value
            ? new Dialog(
                _ => CancelGenerate(),
                new DialogHeader("Generating Verifications..."),
                new DialogBody(
                    new AgentOutputView()
                        .Provider("claude")
                        .Stream(generateStream)
                        .AutoScroll(true)
                        .ShowStatusLabel(true)
                        .Width(Size.Full())
                        .Height(Size.Rem(30))
                ),
                new DialogFooter(
                    isGenerating.Value
                        ? new Button("Cancel").Outline().OnClick(CancelGenerate)
                        : new Button("Done").Primary().OnClick(() => showGenerateDialog.Set(false))
                )
            ).Width(Size.Rem(40))
            : null;

        return new Fragment(mainDialog, generateDialog!);
    }

    private static async Task<bool> CloneRepositoryAsync(string url, string destinationPath)
    {
        try
        {
            var shell = "pwsh";

            if (url.Contains('\'') || url.Contains('"')) return false;

            string cmd;
            if (Directory.Exists(destinationPath))
            {
                cmd = $"git -C '{destinationPath}' pull";
            }
            else
            {
                cmd = $"git clone '{url}' '{destinationPath}'";
            }

            var psi = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = $"-NoProfile -Command \"{cmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
