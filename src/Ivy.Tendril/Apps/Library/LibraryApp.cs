using System;
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
using Ivy.Tendril.Apps.Library.Dialogs;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Library;

public record VaultStatusInfo(
    string VaultPath,
    int TotalMemories,
    int OutdatedMemories,
    int BrokenWikiLinks,
    int OrphanMemories,
    int IncompleteTemplates,
    HashSet<string> OutdatedNoteNames,
    string RawOutput
);

[App(title: "Library", icon: Icons.Library, group: ["Apps"], order: Constants.Library)]
public class LibraryApp : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();
        var jobService = UseService<IJobService>();
        var vaultStatus = UseState<VaultStatusInfo?>(null);
        var isLoading = UseState(true);

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
        var isDeleteOpen = UseState(false);
        var isUpdateMemoriesOpen = UseState(false);
        var projectFiles = UseState<List<string>>(new List<string>());
        var isFilesLoading = UseState(false);

        // Find the vault directory
        var workspaceDir = config.Projects.FirstOrDefault()?.RepoPaths.FirstOrDefault();
        var workingDir = string.IsNullOrEmpty(workspaceDir) ? Directory.GetCurrentDirectory() : workspaceDir;
        var vaultPath = PromptwareHelper.ResolveBrainwaresVaultDir(workingDir);
        var memoriesDir = vaultPath != null ? Path.Combine(vaultPath, "memories") : null;

        void LoadProjectFiles()
        {
            isFilesLoading.Set(true);
            _ = Task.Run(async () =>
            {
                var list = new List<string>();
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "ls-files",
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
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode == 0)
                        {
                            list = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                .Select(line => line.Trim())
                                .Where(line => !string.IsNullOrEmpty(line))
                                .OrderBy(line => line)
                                .ToList();
                        }
                    }
                }
                catch { }

                if (list.Count == 0 && Directory.Exists(workingDir))
                {
                    try
                    {
                        list = Directory.GetFiles(workingDir, "*", SearchOption.AllDirectories)
                            .Select(p => Path.GetRelativePath(workingDir, p).Replace('\\', '/'))
                            .Where(p => !p.StartsWith('.') && !p.Contains("/.") && !p.Contains("node_modules/") && !p.Contains("bin/") && !p.Contains("obj/") && !p.Contains("target/"))
                            .OrderBy(p => p)
                            .ToList();
                    }
                    catch { }
                }

                projectFiles.Set(list);
                isFilesLoading.Set(false);
            });
        }

        void StartMemoryUpdateJob(List<string> selectedFiles)
        {
            if (selectedFiles == null || selectedFiles.Count == 0) return;
            var project = config.Projects.FirstOrDefault()?.Name ?? "Auto";
            var jobArgs = new UpdateMemoriesArgs(project, selectedFiles);
            jobService.StartJob(jobArgs);
            client.Toast($"Started agentic memory update job for {selectedFiles.Count} files.", "Job Started");
        }

        void LoadStatus()
        {
            isLoading.Set(true);
            _ = Task.Run(async () =>
            {
                var status = await RunBwStatusAsync(workingDir, vaultPath);
                vaultStatus.Set(status);
                isLoading.Set(false);
            });
        }

        void RunCommand(string name, string args)
        {
            isOperationRunning.Set(true);
            var vaultArg = vaultPath != null ? $"--vault \"{vaultPath}\" " : "";
            operationLogs.Set($"Running command: bw {vaultArg}{args}...\n");
            _ = Task.Run(async () =>
            {
                try
                {
                    var bwPath = PromptwareHelper.GetBwPath();
                    var psi = new ProcessStartInfo
                    {
                        FileName = bwPath,
                        Arguments = $"{vaultArg}{args}",
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
            | Text.H1("Library").Bold()
            | Text.Muted("Obsidian-style codebase memory index and verification stats");

        // Sidebar files load
        var files = new List<string>();
        if (memoriesDir != null && Directory.Exists(memoriesDir))
        {
            try
            {
                var allMdFiles = Directory.GetFiles(memoriesDir, "*.md", SearchOption.AllDirectories);
                files = allMdFiles
                    .Select(p => {
                        var rel = Path.GetRelativePath(memoriesDir, p);
                        rel = rel.Replace('\\', '/');
                        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        {
                            rel = rel.Substring(0, rel.Length - 3);
                        }
                        return rel;
                    })
                    .Where(f => f != null && !f.StartsWith('.') && !f.Contains("/."))
                    .OrderBy(f => f)
                    .ToList()!;
            }
            catch { }
        }

        // Render main components
        var mainContentView = new ContentView(
            selectedNote,
            vaultStatus.Value,
            isLoading,
            isEditing,
            editContent,
            isOperationRunning,
            operationLogs,
            vaultPath,
            memoriesDir,
            defaultVaultDir,
            ignorePatterns,
            LoadStatus,
            RunCommand,
            SyncAllHashes,
            client,
            isDeleteOpen,
            header,
            workingDir,
            isUpdateMemoriesOpen,
            LoadProjectFiles
        );

        var sidebarView = new SidebarView(
            files,
            selectedNote,
            searchQuery,
            isNewNoteOpen,
            vaultStatus.Value
        );

        var body = new SidebarLayout(
            mainContentView,
            sidebarView
        ).SidebarContentScroll(Scroll.None);

        var elements = new List<object> { body };

        if (isNewNoteOpen.Value)
        {
            elements.Add(new CreateMemoryNoteDialog(isNewNoteOpen, selectedNote, RunCommand, client));
        }

        if (isDeleteOpen.Value && memoriesDir != null)
        {
            elements.Add(new DeleteMemoryNoteDialog(isDeleteOpen, selectedNote, memoriesDir, LoadStatus, client));
        }

        if (isUpdateMemoriesOpen.Value)
        {
            elements.Add(new UpdateMemoriesDialog(
                isUpdateMemoriesOpen,
                projectFiles,
                isFilesLoading,
                LoadProjectFiles,
                StartMemoryUpdateJob,
                client
            ));
        }

        return new Fragment(elements.ToArray());
    }

    private static async Task<VaultStatusInfo?> RunBwStatusAsync(string workingDirectory, string? vaultPath)
    {
        try
        {
            var bwPath = PromptwareHelper.GetBwPath();
            var args = vaultPath != null ? $"--vault \"{vaultPath}\" status" : "status";
            var psi = new ProcessStartInfo
            {
                FileName = bwPath,
                Arguments = args,
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

            var statusVaultPath = "";
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
                    statusVaultPath = line.Substring("Vault path:".Length).Trim(' ', '"');
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

            return new VaultStatusInfo(statusVaultPath, totalMemories, outdated, broken, orphan, incomplete, outdatedNoteNames, stdout);
        }
        catch (Exception ex)
        {
            return new VaultStatusInfo("", 0, 0, 0, 0, 0, new HashSet<string>(), $"Exception running bw status: {ex.Message}");
        }
    }
}
