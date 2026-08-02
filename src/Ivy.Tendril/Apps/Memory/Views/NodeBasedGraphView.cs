using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Memory;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Memory.Views;

public class NodeBasedGraphView : ViewBase
{
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]\|]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled);

    private readonly List<MemoryNote> _allMemories;
    private readonly VaultStatusInfo? _status;
    private readonly IState<string?> _selectedNote;
    private readonly IState<string?> _selectedFile;
    private readonly IState<bool> _isEditing;
    private readonly IState<string> _editContent;
    private readonly Action _onLoadStatus;
    private readonly Action _onSyncAllHashes;
    private readonly IClientProvider _client;
    private readonly IState<bool> _isDeleteOpen;
    private readonly IState<bool> _isAiEditOpen;
    private readonly string _workingDir;
    private readonly IMemoryService _memoryService;

    public NodeBasedGraphView(
        List<MemoryNote> allMemories,
        VaultStatusInfo? status,
        IState<string?> selectedNote,
        IState<string?> selectedFile,
        IState<bool> isEditing,
        IState<string> editContent,
        Action onLoadStatus,
        Action onSyncAllHashes,
        IClientProvider client,
        IState<bool> isDeleteOpen,
        IState<bool> isAiEditOpen,
        string workingDir,
        IMemoryService memoryService)
    {
        _allMemories = allMemories;
        _status = status;
        _selectedNote = selectedNote;
        _selectedFile = selectedFile;
        _isEditing = isEditing;
        _editContent = editContent;
        _onLoadStatus = onLoadStatus;
        _onSyncAllHashes = onSyncAllHashes;
        _client = client;
        _isDeleteOpen = isDeleteOpen;
        _isAiEditOpen = isAiEditOpen;
        _workingDir = workingDir;
        _memoryService = memoryService;
    }

    public override object Build()
    {
        var graphNodes = new List<BrainNode>();
        var graphEdges = new List<BrainEdge>();
        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Add all Memory Notes first so valid notes are clean/outdated
        foreach (var note in _allMemories)
        {
            if (nodeIds.Add(note.Name))
            {
                var isOutdated = _status?.OutdatedNoteNames.Contains(note.Name) ?? false;
                graphNodes.Add(new BrainNode(note.Name, note.Title, "memory", isOutdated ? "outdated" : "clean"));
            }
        }

        // 2. Add edges and any missing relation/file/wiki-link targets
        foreach (var note in _allMemories)
        {
            foreach (var rel in note.Relations)
            {
                if (string.IsNullOrWhiteSpace(rel)) continue;
                var normRel = rel.Trim();
                if (nodeIds.Add(normRel))
                {
                    graphNodes.Add(new BrainNode(normRel, normRel, "memory", "broken"));
                }
                graphEdges.Add(new BrainEdge(note.Name, normRel));
            }

            foreach (var (relPath, _) in note.Targets)
            {
                if (string.IsNullOrWhiteSpace(relPath)) continue;
                var norm = relPath.Replace('\\', '/');
                var fileId = "file:" + norm;
                if (nodeIds.Add(fileId))
                {
                    graphNodes.Add(new BrainNode(fileId, Path.GetFileName(norm), "file", "clean"));
                }
                graphEdges.Add(new BrainEdge(note.Name, fileId));
            }

            var matches = WikiLinkRegex.Matches(note.Content);
            foreach (Match m in matches)
            {
                var target = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(target))
                {
                    if (nodeIds.Add(target))
                    {
                        graphNodes.Add(new BrainNode(target, target, "memory", "broken"));
                    }
                    if (!graphEdges.Any(e => e.Source == note.Name && e.Target == target))
                    {
                        graphEdges.Add(new BrainEdge(note.Name, target));
                    }
                }
            }
        }

        var activeNodeId = _selectedNote.Value ?? (_selectedFile.Value != null ? "file:" + _selectedFile.Value : null);

        var brainMapWidget = new BrainMap()
            .Nodes(graphNodes)
            .Edges(graphEdges)
            .SelectedNodeId(activeNodeId)
            .OnNodeClick(nodeId =>
            {
                if (nodeId.StartsWith("file:"))
                {
                    var file = nodeId.Substring(5);
                    _selectedFile.Set(file);
                    _selectedNote.Set(null);
                }
                else
                {
                    _selectedNote.Set(nodeId);
                    _selectedFile.Set(null);
                }
            });

        object? detailDrawer = null;
        var currentNoteName = _selectedNote.Value;

        if (!string.IsNullOrEmpty(currentNoteName))
        {
            var note = _allMemories.FirstOrDefault(n => string.Equals(n.Name, currentNoteName, StringComparison.OrdinalIgnoreCase));
            if (note != null)
            {
                var isOutdated = _status?.OutdatedNoteNames.Contains(currentNoteName) ?? false;

                var toolbar = Layout.Horizontal().Gap(1)
                    | new Button("Close").Outline().Icon(Icons.X).Small().OnClick(() => _selectedNote.Set(null))
                    | new Button("Edit").Outline().Icon(Icons.Pencil).Small().OnClick(() =>
                      {
                          _editContent.Set(note.Content);
                          _isEditing.Set(true);
                      })
                    | new Button("AI Edit").Outline().Icon(Icons.Sparkles).Small().OnClick(() => _isAiEditOpen.Set(true))
                    | (isOutdated
                       ? (object)new Button("Update Reference").Primary().Icon(Icons.RefreshCw).Small().OnClick(() =>
                         {
                             _memoryService.UpdateMemory(currentNoteName, _workingDir);
                             _client.Toast($"Updated hash for {currentNoteName}", "Updated");
                             _onLoadStatus();
                         })
                       : new Fragment())
                    | new Button("Delete").Destructive().Icon(Icons.Trash2).Small().OnClick(() => _isDeleteOpen.Set(true));

                var drawerContent = _isEditing.Value
                    ? (object)(Layout.Vertical().Gap(2)
                       | _editContent.ToTextInput().Multiline().Height(Size.Px(120))
                       | (Layout.Horizontal().Gap(2).Right()
                          | new Button("Cancel").Outline().Small().OnClick(() => _isEditing.Set(false))
                          | new Button("Save Note").Primary().Icon(Icons.Save).Small().OnClick(() =>
                            {
                                _memoryService.WriteMemory(currentNoteName, _editContent.Value, _workingDir);
                                _isEditing.Set(false);
                                _client.Toast($"Saved note {currentNoteName}", "Saved");
                                _onLoadStatus();
                            })))
                    : new DraftMarkdown(note.Content);

                detailDrawer = new Card(
                    Layout.Vertical().Gap(2).Padding(2).Height(Size.Px(200)).Scroll(Scroll.Auto)
                    | (Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                       | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                          | Text.Block(note.Title).Bold()
                          | new Badge(note.ProjectName).Variant(BadgeVariant.Outline).Small()
                          | (isOutdated
                             ? new Badge("Outdated Reference").Variant(BadgeVariant.Destructive).Small()
                             : new Badge("Synchronized").Variant(BadgeVariant.Success).Small()))
                       | toolbar)
                    | (note.Targets.Count > 0
                       ? Text.Muted($"Linked Code Files: {string.Join(", ", note.Targets.Keys)}").Small()
                       : Text.Muted("No linked code files").Small())
                    | drawerContent
                );
            }
        }
        else if (!string.IsNullOrEmpty(_selectedFile.Value))
        {
            var targetFile = _selectedFile.Value;
            var linked = _allMemories.Where(n => n.Targets.Keys.Any(t => string.Equals(t.Replace('\\', '/'), targetFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))).ToList();

            detailDrawer = new Card(
                Layout.Vertical().Gap(2).Padding(2).Height(Size.Px(140)).Scroll(Scroll.Auto)
                | (Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                   | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                      | Icons.FileCode.ToIcon().Color(Colors.Purple)
                      | Text.Block(Path.GetFileName(targetFile)).Bold()
                      | Text.Muted(targetFile).Small())
                   | new Button("Close").Outline().Icon(Icons.X).Small().OnClick(() => _selectedFile.Set(null)))
                | Text.Muted($"Linked Memories ({linked.Count}): {string.Join(", ", linked.Select(l => l.Name))}")
            );
        }

        if (detailDrawer != null)
        {
            return Layout.Vertical().Size(Size.Full()).Gap(2).RemoveParentPadding()
                | (Layout.Vertical().Size(Size.Grow(1)).RemoveParentPadding() | brainMapWidget)
                | detailDrawer;
        }

        return Layout.Vertical().Size(Size.Full()).RemoveParentPadding() | brainMapWidget;
    }
}
