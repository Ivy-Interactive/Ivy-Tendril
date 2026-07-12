using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps;

[App(title: "Knowledge Library", icon: Icons.Library, group: ["Apps"], order: Constants.KnowledgeLibrary)]
public class KnowledgeLibraryApp : ViewBase
{
    private record VaultStatusInfo(
        string VaultPath,
        int TotalMemories,
        int OutdatedMemories,
        int BrokenWikiLinks,
        int OrphanMemories,
        int IncompleteTemplates,
        HashSet<string> OutdatedNoteNames,
        string RawOutput
    );

    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var vaultStatus = UseState<VaultStatusInfo?>(null);
        var isLoading = UseState(true);
        var uiProcess = UseState<Process?>(null);
        var uiUrl = UseState<string?>(null);

        // Sidebar and search states
        var selectedNote = UseState<string?>(null);
        var searchQuery = UseState<string?>("");
        var editContent = UseState("");
        var isEditing = UseState(false);

        // Operation states
        var isOperationRunning = UseState(false);
        var operationLogs = UseState("");

        // Dialog states
        var isNewNoteOpen = UseState(false);
        var newNoteName = UseState("");
        var newNoteTitle = UseState("");
        var newNoteTags = UseState("");
        var isDeleteOpen = UseState(false);

        // Find the vault directory
        var workspaceDir = config.Projects.FirstOrDefault()?.RepoPaths.FirstOrDefault();
        var workingDir = string.IsNullOrEmpty(workspaceDir) ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(workspaceDir) ?? Directory.GetCurrentDirectory();
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workingDir);
        var memoriesDir = vaultPath != null ? Path.Combine(vaultPath, "memories") : null;

        void LoadStatus()
        {
            isLoading.Set(true);
            _ = Task.Run(async () =>
            {
                var status = await RunBwStatusAsync(workingDir);
                vaultStatus.Set(status);
                isLoading.Set(false);
            });
        }

        void RunCommand(string name, string args)
        {
            isOperationRunning.Set(true);
            operationLogs.Set($"Running command: bw {name} {args}...\n");
            _ = Task.Run(async () =>
            {
                try
                {
                    var bwPath = PromptwareHelper.GetBwPath();
                    var psi = new ProcessStartInfo
                    {
                        FileName = bwPath,
                        Arguments = args,
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                        var stderrTask = proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        var stdout = await stdoutTask;
                        var stderr = await stderrTask;

                        var log = "";
                        if (!string.IsNullOrEmpty(stdout)) log += stdout + "\n";
                        if (!string.IsNullOrEmpty(stderr)) log += "Error:\n" + stderr + "\n";
                        log += $"Process exited with code {proc.ExitCode}.\n";
                        operationLogs.Set(log);
                        
                        if (proc.ExitCode == 0)
                        {
                            client.Toast($"Command '{name}' completed successfully.", "Success");
                        }
                        else
                        {
                            client.Toast($"Command '{name}' failed with exit code {proc.ExitCode}.", "Error");
                        }
                    }
                    else
                    {
                        operationLogs.Set("Failed to start process.\n");
                    }
                }
                catch (Exception ex)
                {
                    operationLogs.Set($"Exception: {ex.Message}\n");
                    client.Toast($"Exception running command: {ex.Message}", "Error");
                }
                finally
                {
                    isOperationRunning.Set(false);
                    LoadStatus();
                }
            });
        }

        void SyncAllHashes()
        {
            isOperationRunning.Set(true);
            operationLogs.Set("Synchronizing all outdated memory hashes...\n");
            _ = Task.Run(async () =>
            {
                var outdated = vaultStatus.Value?.OutdatedNoteNames.ToList() ?? [];
                if (outdated.Count == 0)
                {
                    operationLogs.Set("No outdated memory hashes found.\n");
                    isOperationRunning.Set(false);
                    return;
                }

                var bwPath = PromptwareHelper.GetBwPath();
                var log = "";
                foreach (var note in outdated)
                {
                    log += $"Syncing hashes for memory note: {note}...\n";
                    operationLogs.Set(log);
                    
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = bwPath,
                            Arguments = $"update {note}",
                            WorkingDirectory = workingDir,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            var stdout = await proc.StandardOutput.ReadToEndAsync();
                            var stderr = await proc.StandardError.ReadToEndAsync();
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0)
                            {
                                if (!string.IsNullOrEmpty(stdout)) log += $"{stdout}\n";
                            }
                            else
                            {
                                log += $"Error updating {note}:\n{stderr}\n";
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log += $"Exception: {ex.Message}\n";
                    }
                }
                log += "All hash updates completed.\n";
                operationLogs.Set(log);
                isOperationRunning.Set(false);
                LoadStatus();
            });
        }

        // Load status on mount
        UseEffect(() =>
        {
            LoadStatus();
            return Disposable.Empty;
        }, EffectTrigger.OnMount());

        // Dispose of UI process on unmount
        UseEffect(() =>
        {
            return Disposable.Create(() =>
            {
                if (uiProcess.Value != null)
                {
                    try
                    {
                        uiProcess.Value.Kill(true);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
            });
        });

        // Load content on note change
        UseEffect(() =>
        {
            if (selectedNote.Value != null && memoriesDir != null)
            {
                var noteFile = Path.Combine(memoriesDir, selectedNote.Value + ".md");
                if (File.Exists(noteFile))
                {
                    try
                    {
                        var text = File.ReadAllText(noteFile);
                        editContent.Set(text);
                    }
                    catch
                    {
                        editContent.Set("");
                    }
                }
            }
            isEditing.Set(false);
            return Disposable.Empty;
        }, selectedNote);

        // Read brainwares config.json
        string? defaultVaultDir = null;
        string[]? ignorePatterns = null;
        if (vaultPath != null)
        {
            var configPath = Path.Combine(vaultPath, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("default_vault_dir", out var valDir))
                        defaultVaultDir = valDir.GetString();
                    if (doc.RootElement.TryGetProperty("ignore_patterns", out var valPatterns) && valPatterns.ValueKind == JsonValueKind.Array)
                    {
                        ignorePatterns = valPatterns.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                    }
                }
                catch
                {
                    // Ignore parsing error
                }
            }
        }

        var header = Layout.Vertical().Gap(1)
            | Text.H1("Knowledge Library").Bold()
            | Text.Muted("Obsidian-style codebase memory index and verification stats");

        // UI Dialogs
        object? newNoteDialog = null;
        if (isNewNoteOpen.Value)
        {
            newNoteDialog = new Dialog(
                _ => isNewNoteOpen.Set(false),
                new DialogHeader("Create Memory Note"),
                new DialogBody(
                    Layout.Vertical().Gap(2)
                    | newNoteName.ToTextInput("e.g. authentication-flow")
                        .WithField()
                        .Label("Note Name")
                        .Required()
                    | newNoteTitle.ToTextInput("e.g. Authentication Flow")
                        .WithField()
                        .Label("Title (Optional)")
                    | newNoteTags.ToTextInput("e.g. auth, flow, security")
                        .WithField()
                        .Label("Tags (Optional, comma-separated)")
                ),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => isNewNoteOpen.Set(false)),
                    new Button("Create").Primary().OnClick(() =>
                    {
                        var name = newNoteName.Value?.Trim().Replace(" ", "-").ToLowerInvariant();
                        if (string.IsNullOrEmpty(name))
                        {
                            client.Toast("Name is required", "Validation Error");
                            return;
                        }
                        
                        var args = $"add {name}";
                        var titleVal = newNoteTitle.Value?.Trim();
                        if (!string.IsNullOrEmpty(titleVal))
                        {
                            args += $" --title \"{titleVal}\"";
                        }
                        var tagsVal = newNoteTags.Value?.Trim();
                        if (!string.IsNullOrEmpty(tagsVal))
                        {
                            args += $" --tags \"{tagsVal}\"";
                        }

                        RunCommand("add", args);
                        isNewNoteOpen.Set(false);
                        newNoteName.Set("");
                        newNoteTitle.Set("");
                        newNoteTags.Set("");
                        selectedNote.Set(name);
                    })
                )
            );
        }

        object? deleteDialog = null;
        if (isDeleteOpen.Value && selectedNote.Value != null)
        {
            var noteToDelete = selectedNote.Value;
            deleteDialog = new Dialog(
                _ => isDeleteOpen.Set(false),
                new DialogHeader("Delete Memory Note"),
                new DialogBody(
                    Text.P($"Are you sure you want to permanently delete memory note '{noteToDelete}'?")
                ),
                new DialogFooter(
                    new Button("Cancel").Outline().OnClick(() => isDeleteOpen.Set(false)),
                    new Button("Delete").Destructive().OnClick(() =>
                    {
                        if (memoriesDir != null)
                        {
                            var path = Path.Combine(memoriesDir, noteToDelete + ".md");
                            if (File.Exists(path))
                            {
                                try
                                {
                                    File.Delete(path);
                                    client.Toast($"Memory note '{noteToDelete}' deleted.", "Deleted");
                                }
                                catch (Exception ex)
                                {
                                    client.Toast($"Failed to delete file: {ex.Message}", "Error");
                                }
                            }
                        }
                        isDeleteOpen.Set(false);
                        selectedNote.Set(null);
                        LoadStatus();
                    })
                )
            );
        }

        // 1. Vault not initialized
        if (vaultPath == null)
        {
            var initContent = Layout.Vertical().Padding(new Responsive<Thickness?> { Mobile = new Thickness(6, 0, 6, 0) }).Gap(6)
                   | header
                   | new Spacer().Height(Size.Units(5))
                   | new Card(
                       Layout.Vertical().AlignContent(Align.Center).Gap(4).Padding(6)
                        | Icons.Folder.ToIcon().Size(Size.Units(12)).Color(Colors.Warning)
                        | Text.H3("No Knowledge Vault Initialized").Bold()
                        | Text.Muted("An Obsidian-style vault (.brainwares) is required to track codebase memory notes and code/variant reference hashes in this project.")
                        | (isOperationRunning.Value
                           ? (object)(Layout.Vertical().AlignContent(Align.Center).Gap(2)
                             | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                             | Text.Muted("Initializing vault..."))
                           : new Button("Initialize Vault")
                               .Primary()
                               .Icon(Icons.FolderPlus)
                               .OnClick(() => RunCommand("init", "init")))
                      );

            if (!string.IsNullOrEmpty(operationLogs.Value))
            {
                initContent = initContent
                    | new Card(
                        Layout.Vertical().Padding(4).Gap(2)
                        | Text.H3("Initialization Output").Bold()
                        | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(150))
                            | Text.Block(operationLogs.Value).Small()
                      );
            }

            var initElements = new List<object> { initContent };
            return new Fragment(initElements.ToArray());
        }

        // 2. Vault initialized
        var status = vaultStatus.Value;
        var isClean = status != null && status.OutdatedMemories == 0 && status.BrokenWikiLinks == 0;

        // Stats grid
        object statsGrid;
        if (isLoading.Value)
        {
            statsGrid = Layout.Center().Margin(0, 10)
                        | new Icon(Icons.LoaderCircle).Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                        | Text.Muted("Scanning vault status...");
        }
        else if (status == null)
        {
            statsGrid = Text.Danger("Failed to read vault status.");
        }
        else
        {
            statsGrid = Layout.Horizontal().Gap(4).Wrap(true)
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.TotalMemories.ToString()).Bold()
                      | Text.Muted("Total Memories").Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OutdatedMemories.ToString()).Bold()
                      | (status.OutdatedMemories > 0 ? Text.Danger("Outdated").Bold() : Text.Success("Up to date")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.BrokenWikiLinks.ToString()).Bold()
                      | (status.BrokenWikiLinks > 0 ? Text.Danger("Broken Links").Bold() : Text.Success("Clean Links")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(status.OrphanMemories.ToString()).Bold()
                      | Text.Muted("Orphans").Small()
                  ).Width(Size.Units(40));
        }

        var actionsToolbar = Layout.Horizontal().Gap(2).Wrap(true)
            | new Button("Refresh Status").Outline().Icon(Icons.RefreshCw).OnClick(LoadStatus)
            | (uiProcess.Value == null
               ? new Button("Launch Web UI").Primary().Icon(Icons.ExternalLink).OnClick(() =>
                 {
                     try
                     {
                         var bwPath = PromptwareHelper.GetBwPath();
                         var proc = Process.Start(new ProcessStartInfo
                         {
                             FileName = bwPath,
                             Arguments = "ui",
                             WorkingDirectory = workingDir,
                             UseShellExecute = false,
                             CreateNoWindow = true
                         });
                         uiProcess.Set(proc);
                         uiUrl.Set("http://localhost:5173");
                         client.OpenUrl("http://localhost:5173");
                         client.Toast("Memories UI launched on http://localhost:5173", "UI Started");
                     }
                     catch (Exception ex)
                     {
                         client.Toast($"Failed to launch Web UI: {ex.Message}", "Error");
                     }
                 })
               : new Button("Stop Web UI").Variant(ButtonVariant.Destructive).Icon(Icons.Square).OnClick(() =>
                 {
                     if (uiProcess.Value != null)
                     {
                         try { uiProcess.Value.Kill(true); } catch { }
                         uiProcess.Set(null);
                         uiUrl.Set(null);
                         client.Toast("Memories UI stopped", "UI Deactivated");
                     }
                 }))
            | new Button("Clean Vault").Outline().Icon(Icons.Trash2).OnClick(() => RunCommand("shake", "shake"))
            | new Button("Run Diagnostics").Outline().Icon(Icons.CircleCheck).OnClick(() => RunCommand("doctor", "doctor"))
            | new Button("Bootstrap Index").Outline().Icon(Icons.FolderSync).OnClick(() => RunCommand("index", "index"))
            | (status != null && status.OutdatedMemories > 0
               ? (object)new Button("Sync All Hashes").Primary().Icon(Icons.RefreshCw).OnClick(SyncAllHashes)
               : new Fragment());

        var configContent = Layout.Vertical().Gap(2)
            | Text.H2("Configuration").Bold()
            | (Layout.Vertical().Gap(1)
               | (Layout.Horizontal().Gap(2)
                  | Text.Bold("Vault Directory:")
                  | Text.Muted(vaultPath))
               | (Layout.Horizontal().Gap(2)
                  | Text.Bold("Default Directory Key:")
                  | Text.Muted(defaultVaultDir ?? ".brainwares"))
               | (ignorePatterns != null && ignorePatterns.Length > 0
                  ? Layout.Vertical().Gap(1)
                    | Text.Bold("Ignore Patterns:")
                    | Layout.Horizontal().Gap(2).Wrap(true)
                      | new Fragment(ignorePatterns.Select(p => new Badge(p).Variant(BadgeVariant.Secondary).Small() as object).ToArray())
                  : new Fragment()));

        var dashboardContent = Layout.Vertical().Padding(6).Gap(6)
            | header
            | new Separator()
            | Layout.Vertical().Gap(4)
                | (Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                   | Text.H2("Vault Status").Bold()
                   | (isClean
                      ? new Badge("Vault Verified").Variant(BadgeVariant.Success).Small()
                      : new Badge("Attention Required").Variant(BadgeVariant.Destructive).Small()))
                | statsGrid
            | actionsToolbar
            | (isOperationRunning.Value 
               ? (object)(Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                 | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                 | Text.Muted("Running operation in background..."))
               : new Fragment())
            | new Card(
                  Layout.Vertical().Gap(2).Padding(4)
                  | Text.H3("Command Output / Console Log").Bold()
                  | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(200))
                      | Text.Block(string.IsNullOrEmpty(operationLogs.Value) ? "No logs yet. Run an action to see terminal output." : operationLogs.Value).Small()
              )
            | new Separator()
            | configContent;

        // Note View mode
        object noteViewContent = null!;
        if (selectedNote.Value != null)
        {
            var noteName = selectedNote.Value;
            var noteFile = Path.Combine(memoriesDir!, noteName + ".md");
            var isOutdated = status != null && status.OutdatedNoteNames.Contains(noteName);

            var noteHeader = Layout.Horizontal().Width(Size.Full()).Gap(2).AlignContent(Align.Center)
                | new Button("Back").Icon(Icons.ArrowLeft).Outline().OnClick(() => selectedNote.Set(null))
                | Text.H2(noteName).Bold()
                | (isOutdated ? (object)new Badge("Hashes Outdated").Variant(BadgeVariant.Warning).Small() : new Fragment());

            var noteActionBar = Layout.Horizontal().AlignContent(Align.Center).Gap(2).Padding(1)
                | (isEditing.Value
                   ? new Fragment(
                       new Button("Save").Icon(Icons.Save).Primary().OnClick(() =>
                       {
                           try
                           {
                               File.WriteAllText(noteFile, editContent.Value);
                               isEditing.Set(false);
                               client.Toast("Memory note saved.", "Saved");
                               LoadStatus();
                           }
                           catch (Exception ex)
                           {
                               client.Toast($"Failed to save: {ex.Message}", "Error");
                           }
                       }),
                       new Button("Cancel").Outline().OnClick(() =>
                       {
                           if (File.Exists(noteFile))
                               editContent.Set(File.ReadAllText(noteFile));
                           isEditing.Set(false);
                       })
                     )
                   : new Fragment(
                       new Button("Edit").Icon(Icons.Pencil).Outline().OnClick(() => isEditing.Set(true)),
                       isOutdated
                         ? (object)new Button("Sync Hashes").Icon(Icons.RefreshCw).Primary().OnClick(() => RunCommand("update", $"update {noteName}"))
                         : new Fragment(),
                       new Button("Delete").Icon(Icons.Trash).Variant(ButtonVariant.Destructive).OnClick(() => isDeleteOpen.Set(true))
                     ));

            object noteBody;
            if (isEditing.Value)
            {
                noteBody = editContent.ToTextareaInput("Write your markdown memory here...")
                           .Rows(25)
                           .Width(Size.Full())
                           .WithField();
            }
            else
            {
                var markdownText = File.Exists(noteFile) ? File.ReadAllText(noteFile) : "";
                noteBody = Layout.Vertical().Width(Size.Full()).Padding(4, 2, 4, 2)
                           | new Markdown(markdownText).Article();
            }

            noteViewContent = new HeaderLayout(
                noteHeader,
                new FooterLayout(
                    noteActionBar,
                    Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()) | noteBody
                ).Size(Size.Full())
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        // Sidebar content
        var files = new List<string>();
        if (memoriesDir != null && Directory.Exists(memoriesDir))
        {
            try
            {
                files = Directory.GetFiles(memoriesDir, "*.md")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(f => f != null && !f.StartsWith('.'))
                    .OrderBy(f => f)
                    .ToList()!;
            }
            catch { }
        }

        var filteredList = files;
        if (!string.IsNullOrWhiteSpace(searchQuery.Value))
        {
            var q = searchQuery.Value.ToLowerInvariant();
            filteredList = files.Where(f => f.ToLowerInvariant().Contains(q)).ToList();
        }

        var sidebarHeader = Layout.Vertical().Gap(2).Padding(2)
            | Layout.Horizontal().Gap(2).AlignContent(Align.Center)
                | searchQuery.ToSearchInput().Placeholder("Search memories...").Width(Size.Full())
                | new Button().Icon(Icons.Plus).Primary().OnClick(() => isNewNoteOpen.Set(true));

        object sidebarContent;
        if (filteredList.Count == 0)
        {
            sidebarContent = Layout.Center().Margin(0, 10) | Text.Muted("No memories found");
        }
        else
        {
            sidebarContent = new List(filteredList.Select(f =>
            {
                var item = f;
                var noteIsOutdated = status != null && status.OutdatedNoteNames.Contains(item);
                var rowBadges = Layout.Horizontal().Gap(1)
                    | (noteIsOutdated ? (object)new Badge("Outdated").Variant(BadgeVariant.Warning).Small() : new Fragment());

                return SidebarListRow.Build(
                    item, 
                    rowBadges, 
                    () => selectedNote.Set(item)
                );
            }));
        }

        var mainPanel = selectedNote.Value == null ? dashboardContent : noteViewContent;
        var sidebarPanel = new HeaderLayout(sidebarHeader, sidebarContent);

        var body = new SidebarLayout(
            mainPanel,
            sidebarPanel
        ).SidebarContentScroll(Scroll.None);

        var elements = new List<object> { body };

        if (newNoteDialog != null)
            elements.Add(newNoteDialog);

        if (deleteDialog != null)
            elements.Add(deleteDialog);

        return new Fragment(elements.ToArray());
    }

    private static async Task<VaultStatusInfo?> RunBwStatusAsync(string workingDirectory)
    {
        try
        {
            var bwPath = PromptwareHelper.GetBwPath();
            var psi = new ProcessStartInfo
            {
                FileName = bwPath,
                Arguments = "status",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return null;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var outdatedNoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentMemory = "";

            if (proc.ExitCode != 0)
            {
                return new VaultStatusInfo("", 0, 0, 0, 0, 0, outdatedNoteNames, $"Error: {stderr}\n{stdout}");
            }

            var vaultPath = "";
            var totalMemories = 0;
            var outdated = 0;
            var broken = 0;
            var orphan = 0;
            var incomplete = 0;

            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Memory:"))
                {
                    currentMemory = line.Substring("Memory:".Length).Trim();
                }
                else if (line.Contains("[OUTDATED CODE]") || line.Contains("[MISSING CODE]"))
                {
                    if (!string.IsNullOrEmpty(currentMemory))
                    {
                        outdatedNoteNames.Add(currentMemory);
                    }
                }
                else if (line.StartsWith("Vault path:"))
                {
                    vaultPath = line.Substring("Vault path:".Length).Trim(' ', '"');
                }
                else if (line.StartsWith("Total memories:"))
                {
                    int.TryParse(line.Substring("Total memories:".Length).Trim(), out totalMemories);
                }
                else if (line.StartsWith("Outdated memories:"))
                {
                    int.TryParse(line.Substring("Outdated memories:".Length).Trim(), out outdated);
                }
                else if (line.StartsWith("Broken wiki-links:"))
                {
                    int.TryParse(line.Substring("Broken wiki-links:".Length).Trim(), out broken);
                }
                else if (line.StartsWith("Orphan memories:"))
                {
                    int.TryParse(line.Substring("Orphan memories:".Length).Trim(), out orphan);
                }
                else if (line.StartsWith("Incomplete templates:"))
                {
                    int.TryParse(line.Substring("Incomplete templates:".Length).Trim(), out incomplete);
                }
            }

            return new VaultStatusInfo(vaultPath, totalMemories, outdated, broken, orphan, incomplete, outdatedNoteNames, stdout);
        }
        catch (Exception ex)
        {
            return new VaultStatusInfo("", 0, 0, 0, 0, 0, new HashSet<string>(), $"Exception running bw status: {ex.Message}");
        }
    }
}
