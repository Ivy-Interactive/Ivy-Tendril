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

public class FileMemoryMapView : ViewBase
{
    private static readonly Regex WikiLinkRegex = new(@"\[\[([^\]\|]+)(?:\|[^\]]+)?\]\]", RegexOptions.Compiled);

    private readonly IState<string?> _selectedFile;
    private readonly IState<string?> _selectedNote;
    private readonly List<MemoryNote> _allMemories;
    private readonly VaultStatusInfo? _status;
    private readonly IState<bool> _isEditing;
    private readonly IState<string> _editContent;
    private readonly Action _onLoadStatus;
    private readonly Action _onSyncAllHashes;
    private readonly IClientProvider _client;
    private readonly IState<bool> _isDeleteOpen;
    private readonly IState<bool> _isAiEditOpen;
    private readonly IState<bool> _isLinkFileOpen;
    private readonly string _workingDir;
    private readonly IMemoryService _memoryService;

    public FileMemoryMapView(
        IState<string?> selectedFile,
        IState<string?> selectedNote,
        List<MemoryNote> allMemories,
        VaultStatusInfo? status,
        IState<bool> isEditing,
        IState<string> editContent,
        Action onLoadStatus,
        Action onSyncAllHashes,
        IClientProvider client,
        IState<bool> isDeleteOpen,
        IState<bool> isAiEditOpen,
        IState<bool> isLinkFileOpen,
        string workingDir,
        IMemoryService memoryService)
    {
        _selectedFile = selectedFile;
        _selectedNote = selectedNote;
        _allMemories = allMemories;
        _status = status;
        _isEditing = isEditing;
        _editContent = editContent;
        _onLoadStatus = onLoadStatus;
        _onSyncAllHashes = onSyncAllHashes;
        _client = client;
        _isDeleteOpen = isDeleteOpen;
        _isAiEditOpen = isAiEditOpen;
        _isLinkFileOpen = isLinkFileOpen;
        _workingDir = workingDir;
        _memoryService = memoryService;
    }

    public override object Build()
    {
        var currentFile = _selectedFile.Value;

        if (string.IsNullOrEmpty(currentFile))
        {
            // Overview view when no file is selected
            var totalMem = _allMemories.Count;
            var outdatedCount = _status?.OutdatedMemories ?? 0;
            var cleanLinks = _status?.BrokenLinks ?? 0;
            var orphanCount = _status?.OrphanMemories ?? 0;

            var upToDateCount = Math.Max(0, totalMem - outdatedCount);

            var statsGrid = Layout.Grid().Columns(4).Gap(3)
                | new MetricView("Total Memories", Icons.Brain, ctx => ctx.UseQuery("totalMem", () => System.Threading.Tasks.Task.FromResult(new MetricRecord(totalMem.ToString()))))
                | new MetricView("Up to Date", Icons.Check, ctx => ctx.UseQuery("upToDate", () => System.Threading.Tasks.Task.FromResult(new MetricRecord(upToDateCount.ToString()))))
                | new MetricView("Clean Links", Icons.Link, ctx => ctx.UseQuery("cleanLinks", () => System.Threading.Tasks.Task.FromResult(new MetricRecord(cleanLinks.ToString()))))
                | new MetricView("Orphans", Icons.FileText, ctx => ctx.UseQuery("orphans", () => System.Threading.Tasks.Task.FromResult(new MetricRecord(orphanCount.ToString()))));

            var recentNotesCards = _allMemories.Take(12).Select(note =>
            {
                var isOutdated = _status?.OutdatedNoteNames.Contains(note.Name) ?? false;
                var cardContent = Layout.Vertical().Gap(1)
                    | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                       | Text.Block(note.Title).Bold()
                       | new Badge(note.ProjectName).Variant(BadgeVariant.Outline).Small())
                    | Text.Muted($"Key: {note.Name}").Small()
                    | (note.Targets.Count > 0
                       ? Text.Muted($"Linked files: {string.Join(", ", note.Targets.Keys.Take(2))}").Small()
                       : Text.Muted("No linked files").Small());

                return new Button()
                    .Width(Size.Full())
                    .Content(cardContent)
                    .Outline()
                    .OnClick(() =>
                    {
                        var firstTarget = note.Targets.Keys.FirstOrDefault();
                        if (!string.IsNullOrEmpty(firstTarget))
                        {
                            _selectedFile.Set(firstTarget);
                        }
                        _selectedNote.Set(note.Name);
                    }) as object;
            }).ToArray();

            return Layout.Vertical().Gap(4).Padding(4).Scroll(Scroll.Auto).Size(Size.Full()).RemoveParentPadding()
                | (Layout.Vertical().Gap(1)
                   | Text.H2("Codebase & Promptware Memory Vault").Bold()
                   | Text.Muted("Select a file from the explorer on the left to inspect its memory relations and linked notes."))
                | statsGrid
                | (Layout.Vertical().Gap(2)
                   | Text.H3("Recent Memory Notes").Bold()
                   | Layout.Horizontal().Wrap(true).Gap(2) | new Fragment(recentNotesCards));
        }

        // Selected file view
        var normSelectedFile = currentFile.Replace('\\', '/');
        var fileName = Path.GetFileName(normSelectedFile);

        // Find memories that link to this file
        var linkedMemories = _allMemories.Where(n =>
            n.Targets.Keys.Any(t => string.Equals(t.Replace('\\', '/'), normSelectedFile, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        // Build scoped BrainMap graph for selected file
        var graphNodes = new List<BrainNode>();
        var graphEdges = new List<BrainEdge>();

        var fileNodeId = "file:" + normSelectedFile;
        graphNodes.Add(new BrainNode(fileNodeId, fileName, "file", "clean"));

        foreach (var note in linkedMemories)
        {
            var isOutdated = _status?.OutdatedNoteNames.Contains(note.Name) ?? false;
            graphNodes.Add(new BrainNode(note.Name, note.Title, "memory", isOutdated ? "outdated" : "clean"));
            graphEdges.Add(new BrainEdge(note.Name, fileNodeId));

            // Also add sibling target files of this memory
            foreach (var (relPath, _) in note.Targets)
            {
                var siblingNorm = relPath.Replace('\\', '/');
                if (string.Equals(siblingNorm, normSelectedFile, StringComparison.OrdinalIgnoreCase)) continue;

                var sibFileId = "file:" + siblingNorm;
                if (!graphNodes.Any(n => n.Id == sibFileId))
                {
                    graphNodes.Add(new BrainNode(sibFileId, Path.GetFileName(siblingNorm), "file", "clean"));
                }
                graphEdges.Add(new BrainEdge(note.Name, sibFileId));
            }

            foreach (var rel in note.Relations)
            {
                if (!graphNodes.Any(n => n.Id == rel))
                {
                    graphNodes.Add(new BrainNode(rel, rel, "memory", "clean"));
                }
                graphEdges.Add(new BrainEdge(note.Name, rel));
            }
        }

        var mapWidget = new BrainMap()
            .Nodes(graphNodes)
            .Edges(graphEdges)
            .SelectedNodeId(fileNodeId)
            .OnNodeClick(nodeId =>
            {
                if (nodeId.StartsWith("file:"))
                {
                    var path = nodeId.Substring(5);
                    _selectedFile.Set(path);
                }
                else
                {
                    _selectedNote.Set(nodeId);
                }
            });

        var fileHeader = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
            | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
               | Icons.FileCode.ToIcon().Color(Colors.Purple)
               | (Layout.Vertical().Gap(0)
                  | Text.H2(fileName).Bold()
                  | Text.Muted(normSelectedFile).Small()))
            | (Layout.Horizontal().Gap(2)
               | new Button("Clear Selection").Outline().Icon(Icons.X).OnClick(() => _selectedFile.Set(null))
               | new Button("Link Memory").Primary().Icon(Icons.Link).OnClick(() => _isLinkFileOpen.Set(true)));

        object linkedNotesSection;
        if (linkedMemories.Count == 0)
        {
            linkedNotesSection = new Card(
                Layout.Vertical().AlignContent(Align.Center).Padding(4).Gap(2)
                | Text.Muted($"No memory notes currently target '{normSelectedFile}'.")
                | new Button("Link Existing Memory").Outline().Icon(Icons.Link).OnClick(() => _isLinkFileOpen.Set(true))
            );
        }
        else
        {
            var noteCards = linkedMemories.Select(note =>
            {
                var isSelectedNote = string.Equals(_selectedNote.Value, note.Name, StringComparison.OrdinalIgnoreCase);
                var isOutdated = _status?.OutdatedNoteNames.Contains(note.Name) ?? false;

                var cardHeader = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween)
                    | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                       | Text.H3(note.Title).Bold()
                       | new Badge(note.ProjectName).Variant(BadgeVariant.Outline).Small()
                       | (isOutdated
                          ? new Badge("Outdated Reference").Variant(BadgeVariant.Destructive).Small()
                          : new Badge("Synchronized").Variant(BadgeVariant.Success).Small()))
                    | (Layout.Horizontal().Gap(2)
                       | new Button("Edit").Outline().Icon(Icons.Pencil).Small().OnClick(() =>
                         {
                             _selectedNote.Set(note.Name);
                             _editContent.Set(note.Content);
                             _isEditing.Set(true);
                         })
                       | new Button("AI Edit").Outline().Icon(Icons.Sparkles).Small().OnClick(() =>
                         {
                             _selectedNote.Set(note.Name);
                             _isAiEditOpen.Set(true);
                         })
                       | (isOutdated
                          ? (object)new Button("Update Reference").Primary().Icon(Icons.RefreshCw).Small().OnClick(() =>
                            {
                                _memoryService.UpdateMemory(note.Name, _workingDir);
                                _client.Toast($"Updated reference hash for {note.Name}", "Synchronized");
                                _onLoadStatus();
                            })
                          : new Fragment())
                       | new Button("Delete").Destructive().Icon(Icons.Trash2).Small().OnClick(() =>
                         {
                             _selectedNote.Set(note.Name);
                             _isDeleteOpen.Set(true);
                         }));

                var noteBodyView = _isEditing.Value && isSelectedNote
                    ? (object)(Layout.Vertical().Gap(2)
                       | _editContent.ToTextInput().Multiline().Height(Size.Px(200))
                       | (Layout.Horizontal().Gap(2).Right()
                          | new Button("Cancel").Outline().OnClick(() => _isEditing.Set(false))
                          | new Button("Save Note").Primary().Icon(Icons.Save).OnClick(() =>
                            {
                                _memoryService.WriteMemory(note.Name, _editContent.Value, _workingDir);
                                _isEditing.Set(false);
                                _client.Toast($"Saved note {note.Name}", "Saved");
                                _onLoadStatus();
                            })))
                    : new DraftMarkdown(note.Content);

                return new Card(
                    Layout.Vertical().Gap(3).Padding(3)
                    | cardHeader
                    | noteBodyView
                );
            }).ToArray();

            linkedNotesSection = Layout.Vertical().Gap(3) | new Fragment(noteCards);
        }

        return Layout.Vertical().Gap(3).Padding(3).Scroll(Scroll.Auto).Size(Size.Full()).RemoveParentPadding()
            | fileHeader
            | new Card(Layout.Vertical().Height(Size.Px(280)) | mapWidget)
            | (Layout.Vertical().Gap(2)
               | Text.H3($"Memories Linked to {fileName}").Bold()
               | linkedNotesSection);
    }
}
