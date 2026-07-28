using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using Ivy;
using Ivy.Tendril.Apps.Library.Dialogs;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Memory;

namespace Ivy.Tendril.Apps.Library;

[App(title: "Library", icon: Icons.BookOpen, group: ["Memory"], order: Constants.Library)]
public class LibraryApp : ViewBase
{
    public LibraryApp()
    {
    }

    public override object Build()
    {
        var memoryService = UseService<IMemoryService>();
        var client = UseService<IClientProvider>();

        var selectedNote = UseState<string?>(null);
        var vaultStatus = UseState<VaultStatusInfo?>(null);
        var isLoading = UseState(true);
        var isEditing = UseState(false);
        var editContent = UseState("");
        var isOperationRunning = UseState(false);
        var operationLogs = UseState("");
        var searchQuery = UseState("");
        var projectFilter = UseState<string?>(null);

        var isNewNoteOpen = UseState(false);
        var isDeleteOpen = UseState(false);
        var isUpdateMemoriesOpen = UseState(false);
        var isAiEditOpen = UseState(false);
        var isGraphView = UseState(false);

        var projectFiles = UseState(new List<string>());
        var isFilesLoading = UseState(false);

        var workingDir = Directory.GetCurrentDirectory();
        var vaultPath = memoryService.ResolveVaultPath(workingDir);
        var memoriesDir = Path.Combine(vaultPath, "memories");

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
            isFilesLoading.Set(true);
            try
            {
                var filesList = Directory.GetFiles(workingDir, "*.*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(workingDir, p).Replace('\\', '/'))
                    .Where(p => !p.StartsWith('.') && !p.Contains("/.") && !p.Contains("bin/") && !p.Contains("obj/"))
                    .Take(200)
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
            return Disposable.Empty;
        });

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

        var header = Layout.Vertical().Gap(1)
            | Text.H1("Library").Bold()
            | Text.Muted("Obsidian-style codebase memory index and verification stats");

        var files = new List<string>();
        try
        {
            var memories = memoryService.ListMemories(workingDir, projectFilter.Value);
            files = memories.Select(m => m.Name).ToList();
        }
        catch { }

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
            "Promptwares",
            null,
            LoadStatus,
            (cmd, title) => { },
            SyncAllHashes,
            client,
            isDeleteOpen,
            header,
            workingDir,
            isUpdateMemoriesOpen,
            LoadProjectFiles,
            isGraphView,
            files,
            () => isAiEditOpen.Set(true),
            memoryService
        );

        var sidebarView = new SidebarView(
            files,
            selectedNote,
            searchQuery,
            projectFilter,
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
            elements.Add(new CreateMemoryNoteDialog(isNewNoteOpen, selectedNote, client, memoryService));
        }

        if (isDeleteOpen.Value)
        {
            elements.Add(new DeleteMemoryNoteDialog(isDeleteOpen, selectedNote, LoadStatus, client, memoryService));
        }

        if (isUpdateMemoriesOpen.Value)
        {
            elements.Add(new UpdateMemoriesDialog(
                isUpdateMemoriesOpen,
                projectFiles.Value,
                isFilesLoading,
                LoadProjectFiles,
                filesToUpdate =>
                {
                    client.Toast($"Updating memories for {filesToUpdate.Count} files", "Memories Update");
                },
                client
            ));
        }

        if (isAiEditOpen.Value && selectedNote.Value != null)
        {
            elements.Add(new AiEditMemoryDialog(
                isAiEditOpen,
                selectedNote.Value,
                (noteName, prompt) =>
                {
                    client.Toast($"Editing memory {noteName} via AI", "AI Edit");
                },
                client
            ));
        }

        return new Fragment(elements.ToArray());
    }
}
