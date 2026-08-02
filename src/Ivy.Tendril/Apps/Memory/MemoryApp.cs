using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using Ivy;
using Ivy.Tendril.Apps.Memory.Dialogs;
using Ivy.Tendril.Apps.Memory.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Memory;

namespace Ivy.Tendril.Apps.Memory;

public enum MemoryViewMode
{
    FileBased,
    NodeBased
}

[App(title: "Memory", icon: Icons.Brain, group: ["Apps"], order: Constants.Memory, isVisible: false)]
public class MemoryApp : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var memoryService = UseService<IMemoryService>();
        var jobService = UseService<IJobService>();
        var client = UseService<IClientProvider>();

        var viewMode = UseState(MemoryViewMode.FileBased);
        var selectedSourceKey = UseState<string?>(null);
        var selectedFolderPath = UseState<string?>(null);
        var selectedFolderName = UseState<string?>("Workspace");

        var selectedFile = UseState<string?>(null);
        var selectedNote = UseState<string?>(null);
        var vaultStatus = UseState<VaultStatusInfo?>(null);
        var isLoading = UseState(true);
        var isEditing = UseState(false);
        var editContent = UseState("");
        var isOperationRunning = UseState(false);
        var projectFiles = UseState<List<string>>(new List<string>());
        var isFilesLoading = UseState(false);

        var isNewNoteOpen = UseState(false);
        var isDeleteOpen = UseState(false);
        var isUpdateMemoriesOpen = UseState(false);
        var isAiEditOpen = UseState(false);
        var isLinkFileOpen = UseState(false);
        var isPurgeOpen = UseState(false);
        var isCompactOpen = UseState(false);

        var workingDir = Directory.GetCurrentDirectory();
        var projects = config.Settings.Projects ?? new List<ProjectConfig>();
        var configuredPromptwares = config.Settings.Promptwares;

        void LoadStatus()
        {
            try
            {
                var status = memoryService.GetStatus(workingDir);
                vaultStatus.Set(status);
            }
            catch (Exception ex)
            {
                client.Toast($"Failed to load vault status: {ex.Message}", "Error");
            }
            finally
            {
                isLoading.Set(false);
            }
        }

        void SyncAllHashes()
        {
            isOperationRunning.Set(true);
            try
            {
                if (vaultStatus.Value != null)
                {
                    foreach (var noteName in vaultStatus.Value.OutdatedNoteNames)
                    {
                        memoryService.UpdateMemory(noteName, workingDir);
                    }
                }
                client.Toast("Synchronized memory reference hashes", "Vault Synchronized");
                LoadStatus();
            }
            catch (Exception ex)
            {
                client.Toast($"Sync error: {ex.Message}", "Error");
            }
            finally
            {
                isOperationRunning.Set(false);
            }
        }

        void LoadProjectFiles()
        {
            var targetDir = !string.IsNullOrEmpty(selectedFolderPath.Value) && Directory.Exists(selectedFolderPath.Value)
                ? selectedFolderPath.Value
                : workingDir;

            isFilesLoading.Set(true);
            try
            {
                var filesList = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(targetDir, p).Replace('\\', '/'))
                    .Where(p => !p.StartsWith('.') && !p.Contains("/.") && !p.Contains("bin/") && !p.Contains("obj/") && !p.Contains("node_modules/"))
                    .Take(1000)
                    .ToList();
                projectFiles.Set(filesList);
            }
            catch { }
            finally
            {
                isFilesLoading.Set(false);
            }
        }

        UseEffect(() =>
        {
            LoadStatus();
            LoadProjectFiles();
            return Disposable.Empty;
        }, selectedFolderPath);

        UseEffect(() =>
        {
            if (selectedNote.Value != null)
            {
                var note = memoryService.ReadMemory(selectedNote.Value, workingDir);
                if (note != null)
                {
                    editContent.Set(note.Content);
                }
            }
            isEditing.Set(false);
            return Disposable.Empty;
        }, selectedNote);

        var sourceOptions = FileExplorerView.GetAllSourceOptions(projects, workingDir, configuredPromptwares);
        var currentSourceMatch = sourceOptions.FirstOrDefault(o =>
            string.Equals(o.Id, selectedSourceKey.Value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Label, selectedSourceKey.Value, StringComparison.OrdinalIgnoreCase))
            ?? sourceOptions.FirstOrDefault();

        var selectedSourceName = currentSourceMatch?.Name;
        var selectedSourcePath = currentSourceMatch?.Path;

        var availableMemories = new List<MemoryNote>();
        try
        {
            availableMemories = memoryService.ListMemories(workingDir).ToList();
        }
        catch { }

        List<MemoryNote> allMemories;
        if (currentSourceMatch != null)
        {
            var sourceName = currentSourceMatch.Name;
            var sourcePath = currentSourceMatch.Path?.Replace('\\', '/').TrimEnd('/');

            allMemories = availableMemories.Where(m =>
                string.Equals(m.ProjectName, sourceName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(sourcePath) && m.Targets.Keys.Any(t =>
                {
                    var normTarget = t.Replace('\\', '/');
                    return normTarget.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase) ||
                           sourcePath.StartsWith(normTarget, StringComparison.OrdinalIgnoreCase);
                }))
            ).ToList();
        }
        else
        {
            allMemories = availableMemories;
        }

        var explorerView = new FileExplorerView(
            projects,
            configuredPromptwares,
            selectedSourceKey,
            allMemories,
            selectedFile,
            workingDir
        );

        object activeView = viewMode.Value == MemoryViewMode.FileBased
            ? new FileMemoryMapView(
                selectedFile,
                selectedNote,
                allMemories,
                vaultStatus.Value,
                isEditing,
                editContent,
                LoadStatus,
                SyncAllHashes,
                client,
                isDeleteOpen,
                isAiEditOpen,
                isLinkFileOpen,
                workingDir,
                memoryService
              )
            : new NodeBasedGraphView(
                allMemories,
                vaultStatus.Value,
                selectedNote,
                selectedFile,
                isEditing,
                editContent,
                LoadStatus,
                SyncAllHashes,
                client,
                isDeleteOpen,
                isAiEditOpen,
                workingDir,
                memoryService
              );

        var selectedIndex = viewMode.Value == MemoryViewMode.FileBased ? 0 : 1;

        var mainContentTabs = Layout.Tabs(
            new Tab("File-Based", viewMode.Value == MemoryViewMode.FileBased ? activeView : null),
            new Tab("Node-Based", viewMode.Value == MemoryViewMode.NodeBased ? activeView : null)
        )
        .SelectedIndex(selectedIndex)
        .OnSelect(index => viewMode.Set(index == 0 ? MemoryViewMode.FileBased : MemoryViewMode.NodeBased))
        .Variant(TabsVariant.Content)
        .RemoveParentPadding()
        .Padding(0);

        // Outdated memories warning banner
        object? outdatedBanner = null;
        if (vaultStatus.Value != null && vaultStatus.Value.OutdatedMemories > 0)
        {
            outdatedBanner = Layout.Horizontal()
                .AlignContent(Align.SpaceBetween)
                .Padding(2, 4)
                .Background(Colors.Amber)
                .Border(Colors.Amber)
                | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                   | Icons.TriangleAlert.ToIcon().Color(Colors.Amber)
                   | Text.Block($"Found {vaultStatus.Value.OutdatedMemories} outdated memory note(s) needing reference sync or documentation update."))
                | (Layout.Horizontal().Gap(2)
                   | new Button("Sync Hashes").Outline().Small().OnClick(() => SyncAllHashes())
                   | new Button("Update Job").Primary().Small().Icon(Icons.Zap).OnClick(() =>
                     {
                         try
                         {
                             var filesToUpdate = string.Join(",", vaultStatus.Value.OutdatedNoteNames);
                             jobService.StartJob(new UpdateMemoriesArgs(selectedSourceName ?? "Workspace", filesToUpdate));
                             client.Toast("Launched UpdateMemories job", "Job Started");
                         }
                         catch (Exception ex)
                         {
                             client.Toast($"Failed to launch job: {ex.Message}", "Error");
                         }
                     }));
        }

        // Floating Control Island centered at bottom
        var floatingControlIsland = Layout.Horizontal()
            .AlignContent(Align.Center)
            .Gap(2)
            | new Button("New Note").Primary().Small().Icon(Icons.Plus).OnClick(() => isNewNoteOpen.Set(true))
            | new Button("Update Job").Outline().Small().Icon(Icons.Zap).OnClick(() =>
              {
                  try
                  {
                      var filesToUpdate = vaultStatus.Value?.OutdatedNoteNames != null
                          ? string.Join(",", vaultStatus.Value.OutdatedNoteNames)
                          : "";
                      jobService.StartJob(new UpdateMemoriesArgs(selectedSourceName ?? "Workspace", filesToUpdate));
                      client.Toast("Launched UpdateMemories job", "Job Started");
                  }
                  catch (Exception ex)
                  {
                      client.Toast($"Failed to launch job: {ex.Message}", "Error");
                  }
              })
            | new Button("Sync Hashes").Outline().Small().Icon(Icons.RefreshCw).OnClick(() => SyncAllHashes())
            | new Button("Compact").Outline().Small().Icon(Icons.Minimize2).OnClick(() => isCompactOpen.Set(true))
            | new Button("Purge All").Destructive().Small().Icon(Icons.Trash2).OnClick(() => isPurgeOpen.Set(true));

        var mainLayoutArea = Layout.Vertical().Size(Size.Full())
            | (outdatedBanner != null ? outdatedBanner : null)
            | mainContentTabs
            | (Layout.Horizontal().AlignContent(Align.Center).Width(Size.Full()).Height(Size.Shrink()).Padding(1)
               | floatingControlIsland);

        var rootLayout = new SidebarLayout(
            mainLayoutArea,
            explorerView
        ).SidebarContentScroll(Scroll.None);

        if (isNewNoteOpen.Value)
        {
            rootLayout |= new CreateMemoryNoteDialog(isNewNoteOpen, selectedNote, client, memoryService);
        }

        if (isDeleteOpen.Value)
        {
            rootLayout |= new DeleteMemoryNoteDialog(isDeleteOpen, selectedNote, LoadStatus, client, memoryService);
        }

        if (isPurgeOpen.Value)
        {
            rootLayout |= new PurgeMemoriesDialog(isPurgeOpen, () =>
            {
                int count = memoryService.PurgeMemories(workingDir, selectedSourceName);
                client.Toast($"Purged {count} memory note(s)", "Purge Complete");
                LoadStatus();
            });
        }

        if (isCompactOpen.Value)
        {
            rootLayout |= new CompactMemoriesDialog(isCompactOpen, () =>
            {
                int count = memoryService.CompactMemories(workingDir, selectedSourceName);
                client.Toast($"Compacted {count} memory note(s)", "Compaction Complete");
                LoadStatus();
            });
        }

        if (isUpdateMemoriesOpen.Value)
        {
            rootLayout |= new UpdateMemoriesDialog(
                isUpdateMemoriesOpen,
                projectFiles.Value,
                isFilesLoading,
                LoadProjectFiles,
                filesToUpdate =>
                {
                    client.Toast($"Updating memories for {filesToUpdate.Count} file(s)", "Update Started");
                },
                client
            );
        }

        if (isAiEditOpen.Value && selectedNote.Value != null)
        {
            rootLayout |= new AiEditMemoryDialog(
                isAiEditOpen,
                selectedNote.Value,
                (noteName, prompt) =>
                {
                    client.Toast($"Editing memory {noteName} via AI", "AI Edit");
                },
                client
            );
        }

        if (isLinkFileOpen.Value && selectedFile.Value != null)
        {
            rootLayout |= new LinkFileToMemoryDialog(
                isLinkFileOpen,
                selectedFile.Value,
                allMemories,
                LoadStatus,
                client,
                memoryService
            );
        }

        return rootLayout;
    }
}
