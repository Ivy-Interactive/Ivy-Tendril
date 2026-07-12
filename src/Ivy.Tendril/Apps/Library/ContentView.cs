using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Library;

public class ContentView(
    IState<string?> selectedNote,
    VaultStatusInfo? status,
    IState<bool> isLoading,
    IState<bool> isEditing,
    IState<string> editContent,
    IState<bool> isOperationRunning,
    IState<string> operationLogs,
    string? vaultPath,
    string? memoriesDir,
    string? defaultVaultDir,
    string[]? ignorePatterns,
    Action onLoadStatus,
    Action<string, string> onRunCommand,
    Action onSyncAllHashes,
    IClientProvider client,
    IState<bool> isDeleteOpen,
    object headerView,
    string workingDir,
    IState<bool> isUpdateMemoriesOpen,
    Action onLoadFiles) : ViewBase
{
    public override object Build()
    {
        // 1. Vault not initialized
        if (vaultPath == null)
        {
            var initContent = Layout.Vertical()
                   | headerView
                   | new Spacer().Height(Size.Units(5))
                   | new Card(
                       Layout.Vertical().AlignContent(Align.Center)
                       | Icons.Folder.ToIcon().Size(Size.Units(12)).Color(Colors.Warning)
                       | Text.H3("No Knowledge Vault Initialized").Bold()
                       | Text.Muted("An Obsidian-style vault (Promptwares) is required to track codebase memory notes and code/variant reference hashes in this project.")
                       | (isOperationRunning.Value
                          ? (object)(Layout.Vertical().AlignContent(Align.Center)
                            | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                            | Text.Muted("Initializing vault..."))
                          : new Button("Initialize Vault")
                              .Primary()
                              .Icon(Icons.FolderPlus)
                              .OnClick(() => onRunCommand("init", "init")))
                     );

            if (!string.IsNullOrEmpty(operationLogs.Value))
            {
                initContent = initContent
                    | new Card(
                        Layout.Vertical()
                        | Text.H3("Initialization Output").Bold()
                        | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(150))
                            | Text.Block(operationLogs.Value).Small()
                      );
            }

            return initContent;
        }

        // 2. Vault initialized
        var isClean = status != null && status.OutdatedMemories == 0 && status.BrokenWikiLinks == 0;

        if (selectedNote.Value != null)
        {
            // Note details panel
            var noteName = selectedNote.Value;
            var noteFile = Path.Combine(memoriesDir!, noteName + ".md");
            var isOutdated = status != null && status.OutdatedNoteNames.Contains(noteName);

            var noteHeader = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.Center)
                | new Button("Back").Icon(Icons.ArrowLeft).Outline().OnClick(() => selectedNote.Set(null))
                | Text.H2(noteName).Bold()
                | (isOutdated ? (object)new Badge("Hashes Outdated").Variant(BadgeVariant.Warning).Small() : new Fragment());

            var noteActionBar = Layout.Horizontal().AlignContent(Align.Center)
                | (isEditing.Value
                   ? new Fragment(
                       new Button("Save").Icon(Icons.Save).Primary().OnClick(() =>
                       {
                           try
                           {
                               File.WriteAllText(noteFile, editContent.Value);
                               isEditing.Set(false);
                               client.Toast("Memory note saved.", "Saved");
                               onLoadStatus();
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
                         ? (object)new Button("Sync Hashes").Icon(Icons.RefreshCw).Primary().OnClick(() => onRunCommand("update", $"update {noteName}"))
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
                noteBody = Layout.Vertical().Width(Size.Full())
                           | new Markdown(markdownText).Article();
            }

            return new HeaderLayout(
                noteHeader,
                new FooterLayout(
                    noteActionBar,
                    Layout.Vertical().Scroll(Scroll.Auto).Width(Size.Full()) | noteBody
                ).Size(Size.Full())
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        // Stats grid
        object statsGrid;
        if (isLoading.Value)
        {
            statsGrid = Layout.Center()
                        | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                        | Text.Muted("Scanning vault status...");
        }
        else if (status == null)
        {
            statsGrid = Text.Danger("Failed to read vault status.");
        }
        else
        {
            statsGrid = Layout.Horizontal().Wrap(true)
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

        var actionsToolbar = Layout.Horizontal().Wrap(true)
            | new Button("Refresh Status").Outline().Icon(Icons.RefreshCw).OnClick(onLoadStatus)
            | new Button("Clean Vault").Outline().Icon(Icons.Trash2).OnClick(() => onRunCommand("shake", "shake"))
            | new Button("Run Diagnostics").Outline().Icon(Icons.CircleCheck).OnClick(() => onRunCommand("doctor", "doctor"))
            | new Button("Bootstrap Index").Outline().Icon(Icons.FolderSync).OnClick(() => onRunCommand("index", "index"))
            | new Button("Agentic Update").Outline().Icon(Icons.Sparkles).OnClick(() =>
              {
                  onLoadFiles();
                  isUpdateMemoriesOpen.Set(true);
              })
            | (status != null && status.OutdatedMemories > 0
               ? (object)new Button("Sync All Hashes").Primary().Icon(Icons.RefreshCw).OnClick(onSyncAllHashes)
               : new Fragment());

        var configContent = Layout.Vertical()
            | Text.H2("Configuration").Bold()
            | (Layout.Vertical()
               | (Layout.Horizontal()
                  | Text.Bold("Vault Directory:")
                  | Text.Muted(vaultPath))
               | (Layout.Horizontal()
                  | Text.Bold("Default Directory Key:")
                  | Text.Muted(defaultVaultDir ?? "Promptwares"))
               | (ignorePatterns != null && ignorePatterns.Length > 0
                  ? Layout.Vertical()
                    | Text.Bold("Ignore Patterns:")
                    | Layout.Horizontal().Wrap(true)
                      | new Fragment(ignorePatterns.Select(p => new Badge(p).Variant(BadgeVariant.Secondary).Small() as object).ToArray())
                  : new Fragment()));

        return Layout.Vertical()
            | headerView
            | new Separator()
            | Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Center)
                   | Text.H2("Vault Status").Bold()
                   | (isClean
                      ? new Badge("Vault Verified").Variant(BadgeVariant.Success).Small()
                      : new Badge("Attention Required").Variant(BadgeVariant.Destructive).Small()))
                | statsGrid
            | actionsToolbar
            | (isOperationRunning.Value 
               ? (object)(Layout.Horizontal().AlignContent(Align.Center)
                 | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                 | Text.Muted("Running operation in background..."))
               : new Fragment())
            | new Card(
                  Layout.Vertical()
                  | Text.H3("Command Output / Console Log").Bold()
                  | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(200))
                      | Text.Block(string.IsNullOrEmpty(operationLogs.Value) ? "No logs yet. Run an action to see terminal output." : operationLogs.Value).Small()
              )
            | new Separator()
            | configContent;
       }
}
