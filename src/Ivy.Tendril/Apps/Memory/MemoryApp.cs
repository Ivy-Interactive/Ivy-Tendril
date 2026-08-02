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
        var operationLogs = UseState("");
        var searchQuery = UseState("");
        var projectFilter = UseState<string?>(null);
        var onlyLinkedFilter = UseState(false);

        var isNewNoteOpen = UseState(false);
        var isDeleteOpen = UseState(false);
        var isUpdateMemoriesOpen = UseState(false);
        var isAiEditOpen = UseState(false);
        var isLinkFileOpen = UseState(false);

        var projectFiles = UseState(new List<string>());
        var isFilesLoading = UseState(false);

        var projects = config.Settings.Projects;
        var configuredPromptwares = config.Settings.Promptwares;
        var workingDir = Directory.GetCurrentDirectory();

        void LoadStatus()
        {
            isLoading.Set(true);
            try
            {
                var status = memoryService.GetStatus(workingDir, projectFilter.Value);
                vaultStatus.Set(status);
            }
            catch (Exception ex)
            {
                operationLogs.Set($"Error scanning vault status: {ex.Message}");
            }
            finally
            {
                isLoading.Set(false);
            }
        }

        void SyncAllHashes()
        {
            if (vaultStatus.Value == null || vaultStatus.Value.OutdatedNoteNames.Count == 0) return;
            isOperationRunning.Set(true);
            try
            {
                foreach (var noteName in vaultStatus.Value.OutdatedNoteNames)
                {
                    memoryService.UpdateMemory(noteName, workingDir, projectFilter.Value);
                }
                client.Toast($"Synchronized reference hashes for {vaultStatus.Value.OutdatedNoteNames.Count} note(s)", "Vault Synchronized");
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
                var note = memoryService.ReadMemory(selectedNote.Value, workingDir, projectFilter.Value);
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

        var availableMemories = new List<MemoryNote>();
        try
        {
            availableMemories = memoryService.ListMemories(workingDir).ToList();
        }
        catch { }

        var allMemories = string.IsNullOrEmpty(selectedSourceName)
            ? availableMemories
            : availableMemories.Where(m =>
                string.Equals(m.ProjectName, selectedSourceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m.ProjectName, "workspace", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(m.ProjectName)
            ).ToList();

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

        var rootLayout = new SidebarLayout(
            mainContentTabs,
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
