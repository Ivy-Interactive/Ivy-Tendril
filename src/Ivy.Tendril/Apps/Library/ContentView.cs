using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ivy;
using Ivy.Tendril.Apps.Library.Dialogs;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Memory;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Library;

public class ContentView : ViewBase
{
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]\|]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled);

    private readonly IState<string?> _selectedNote;
    private readonly VaultStatusInfo? _status;
    private readonly IState<bool> _isLoading;
    private readonly IState<bool> _isEditing;
    private readonly IState<string> _editContent;
    private readonly IState<bool> _isOperationRunning;
    private readonly IState<string> _operationLogs;
    private readonly string? _vaultPath;
    private readonly string? _memoriesDir;
    private readonly string? _defaultVaultDir;
    private readonly string[]? _ignorePatterns;
    private readonly Action _onLoadStatus;
    private readonly Action<string, string> _onRunCommand;
    private readonly Action _onSyncAllHashes;
    private readonly IClientProvider _client;
    private readonly IState<bool> _isDeleteOpen;
    private readonly object _header;
    private readonly string _workingDir;
    private readonly IState<bool> _isUpdateMemoriesOpen;
    private readonly Action _onLoadFiles;
    private readonly IState<bool> _isGraphView;
    private readonly List<string> _noteIds;
    private readonly Action _onStartMemoryEditJob;
    private readonly IMemoryService _memoryService;

    public ContentView(
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
        object header,
        string workingDir,
        IState<bool> isUpdateMemoriesOpen,
        Action onLoadFiles,
        IState<bool> isGraphView,
        List<string> noteIds,
        Action onStartMemoryEditJob,
        IMemoryService memoryService)
    {
        _selectedNote = selectedNote;
        _status = status;
        _isLoading = isLoading;
        _isEditing = isEditing;
        _editContent = editContent;
        _isOperationRunning = isOperationRunning;
        _operationLogs = operationLogs;
        _vaultPath = vaultPath;
        _memoriesDir = memoriesDir;
        _defaultVaultDir = defaultVaultDir;
        _ignorePatterns = ignorePatterns;
        _onLoadStatus = onLoadStatus;
        _onRunCommand = onRunCommand;
        _onSyncAllHashes = onSyncAllHashes;
        _client = client;
        _isDeleteOpen = isDeleteOpen;
        _header = header;
        _workingDir = workingDir;
        _isUpdateMemoriesOpen = isUpdateMemoriesOpen;
        _onLoadFiles = onLoadFiles;
        _isGraphView = isGraphView;
        _noteIds = noteIds;
        _onStartMemoryEditJob = onStartMemoryEditJob;
        _memoryService = memoryService;
    }

    public override object Build()
    {
        var currentNote = _selectedNote.Value;
        if (!string.IsNullOrEmpty(currentNote))
        {
            var note = _memoryService.ReadMemory(currentNote, _workingDir);
            var noteTitle = note?.Title ?? currentNote;
            var rawContent = note?.Content ?? "";

            var isOutdated = _status?.OutdatedNoteNames.Contains(currentNote) ?? false;

            if (_isEditing.Value)
            {
                return new HeaderLayout(
                    Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                    | Text.H2($"Editing: {noteTitle}").Bold()
                    | (Layout.Horizontal().Gap(2)
                       | new Button("Cancel").Outline().OnClick(() => _isEditing.Set(false))
                       | new Button("Save").Primary().Icon(Icons.Save).OnClick(() =>
                         {
                             _memoryService.WriteMemory(currentNote, _editContent.Value, _workingDir);
                             _isEditing.Set(false);
                             _client.Toast($"Saved {currentNote}", "Memory Saved");
                             _onLoadStatus();
                         })),
                    Layout.Vertical().Size(Size.Full())
                    | _editContent.ToTextInput().Multiline().Size(Size.Full())
                ).Scroll(Scroll.None).Size(Size.Full());
            }

            var noteToolbar = Layout.Horizontal().Wrap(true).Gap(2)
                | new Button("Back to Dashboard").Outline().Icon(Icons.ArrowLeft).OnClick(() => _selectedNote.Set(null))
                | new Button("Edit").Outline().Icon(Icons.Pencil).OnClick(() =>
                  {
                      _editContent.Set(rawContent);
                      _isEditing.Set(true);
                  })
                | new Button("AI Edit").Outline().Icon(Icons.Sparkles).OnClick(_onStartMemoryEditJob)
                | (isOutdated
                   ? (object)new Button("Update Reference").Primary().Icon(Icons.RefreshCw).OnClick(() =>
                     {
                         _memoryService.UpdateMemory(currentNote, _workingDir);
                         _client.Toast($"Updated reference hash for {currentNote}", "Hash Synchronized");
                         _onLoadStatus();
                     })
                   : new Fragment())
                | new Button("Delete").Destructive().Icon(Icons.Trash2).OnClick(() => _isDeleteOpen.Set(true));

            var noteHeader = Layout.Vertical()
                | (Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                   | (Layout.Horizontal().AlignContent(Align.Center)
                      | Text.H1(noteTitle).Bold()
                      | (isOutdated
                         ? new Badge("Outdated Reference").Variant(BadgeVariant.Destructive).Small()
                         : new Badge("Synchronized").Variant(BadgeVariant.Success).Small()))
                   | noteToolbar);

            return new HeaderLayout(
                noteHeader,
                Layout.Vertical().Scroll(Scroll.Auto).Size(Size.Full())
                | new DraftMarkdown(rawContent)
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        var isClean = _status != null && _status.OutdatedMemories == 0 && _status.BrokenLinks == 0;

        var graphNodes = new List<BrainNode>();
        var graphEdges = new List<BrainEdge>();

        if (_memoriesDir != null && Directory.Exists(_memoriesDir))
        {
            var memories = _memoryService.ListMemories(_workingDir).ToList();
            foreach (var note in memories)
            {
                var isOutdated = _status?.OutdatedNoteNames.Contains(note.Name) ?? false;
                graphNodes.Add(new BrainNode(note.Name, note.Title, "memory", isOutdated ? "outdated" : "clean"));

                foreach (var rel in note.Relations)
                {
                    graphEdges.Add(new BrainEdge(note.Name, rel));
                }

                foreach (var (relPath, _) in note.Targets)
                {
                    var fileId = "file:" + relPath;
                    if (!graphNodes.Any(n => n.Id == fileId))
                    {
                        graphNodes.Add(new BrainNode(fileId, Path.GetFileName(relPath), "file", "clean"));
                    }
                    graphEdges.Add(new BrainEdge(note.Name, fileId));
                }

                var matches = WikiLinkRegex.Matches(note.Content);
                foreach (Match m in matches)
                {
                    var target = m.Groups[1].Value.Trim();
                    if (!graphEdges.Any(e => e.Source == note.Name && e.Target == target))
                    {
                        graphEdges.Add(new BrainEdge(note.Name, target));
                    }
                }
            }
        }

        object statsGrid;
        if (_isLoading.Value)
        {
            statsGrid = Layout.Center()
                        | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                        | Text.Muted("Scanning vault status...");
        }
        else if (_status == null)
        {
            statsGrid = Text.Danger("Failed to read vault status.");
        }
        else
        {
            statsGrid = Layout.Horizontal().Wrap(true)
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(_status.TotalMemories.ToString()).Bold()
                      | Text.Muted("Total Memories").Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(_status.OutdatedMemories.ToString()).Bold()
                      | (_status.OutdatedMemories > 0 ? Text.Danger("Outdated").Bold() : Text.Success("Up to date")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(_status.BrokenLinks.ToString()).Bold()
                      | (_status.BrokenLinks > 0 ? Text.Danger("Broken Links").Bold() : Text.Success("Clean Links")).Small()
                  ).Width(Size.Units(40))
                | new Card(
                      Layout.Vertical().AlignContent(Align.Center)
                      | Text.H2(_status.OrphanMemories.ToString()).Bold()
                      | Text.Muted("Orphans").Small()
                  ).Width(Size.Units(40));
        }

        var actionsToolbar = Layout.Horizontal().Wrap(true).Gap(2)
            | new Button("Refresh").Outline().Icon(Icons.RefreshCw).OnClick(_onLoadStatus)
            | new Button("Agentic Update").Outline().Icon(Icons.Sparkles).OnClick(() =>
              {
                  _onLoadFiles();
                  _isUpdateMemoriesOpen.Set(true);
              })
            | (_status != null && _status.OutdatedMemories > 0
               ? (object)new Button("Sync Hashes").Primary().Icon(Icons.RefreshCw).OnClick(_onSyncAllHashes)
               : new Fragment());

        var headerRow = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().AlignContent(Align.Center) | _isGraphView.ToSwitchInput(label: "Brain Map"))
            | actionsToolbar;

        if (_isGraphView.Value)
        {
            var brainMapWidget = new BrainMap()
                .Nodes(graphNodes)
                .Edges(graphEdges)
                .SelectedNodeId(null)
                .OnNodeClick(nodeId =>
                {
                    if (_noteIds.Contains(nodeId))
                    {
                        _selectedNote.Set(nodeId);
                    }
                    else
                    {
                        _client.Toast($"Selected node: {nodeId}", "Graph Node");
                    }
                });

            return new HeaderLayout(
                headerRow,
                Layout.Vertical().Size(Size.Full()) | brainMapWidget
            ).Scroll(Scroll.None).Size(Size.Full());
        }

        var mainBody = Layout.Vertical()
            | (Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.Center)
                   | Text.H2("Vault Status").Bold()
                   | (isClean
                      ? new Badge("Vault Verified").Variant(BadgeVariant.Success).Small()
                      : new Badge("Attention Required").Variant(BadgeVariant.Destructive).Small()))
                | statsGrid)
            | (_isOperationRunning.Value 
               ? (object)(Layout.Horizontal().AlignContent(Align.Center)
                 | Icons.LoaderCircle.ToIcon().Color(Colors.Primary).WithAnimation(AnimationType.Rotate)
                 | Text.Muted("Running operation..."))
               : new Fragment())
            | new Card(
                  Layout.Vertical()
                  | Text.H3("Command Output / Console Log").Bold()
                  | Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(200))
                      | Text.Block(string.IsNullOrEmpty(_operationLogs.Value) ? "No logs yet." : _operationLogs.Value).Small()
              );

        return new HeaderLayout(
            headerRow,
            Layout.Vertical().Scroll(Scroll.Auto).Size(Size.Full()) | mainBody
        ).Scroll(Scroll.None).Size(Size.Full());
    }
}
