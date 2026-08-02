using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ivy;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.Memory.Views;

public class FileExplorerTreeNode
{
    public string Name { get; set; } = "";
    public Dictionary<string, FileExplorerTreeNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Path, string Name, List<MemoryNote> Memories)> Files { get; } = new();
}

public class FileExplorerView : ViewBase
{
    private readonly List<string> _allFiles;
    private readonly List<MemoryNote> _memories;
    private readonly IState<string?> _selectedFile;
    private readonly IState<string> _searchQuery;
    private readonly IState<bool> _onlyLinkedFilter;
    private readonly IState<string?> _projectFilter;
    private readonly Option<string>[] _projectOptions;

    public FileExplorerView(
        List<string> allFiles,
        List<MemoryNote> memories,
        IState<string?> selectedFile,
        IState<string> searchQuery,
        IState<bool> onlyLinkedFilter,
        IState<string?> projectFilter,
        Option<string>[] projectOptions)
    {
        _allFiles = allFiles;
        _memories = memories;
        _selectedFile = selectedFile;
        _searchQuery = searchQuery;
        _onlyLinkedFilter = onlyLinkedFilter;
        _projectFilter = projectFilter;
        _projectOptions = projectOptions;
    }

    public override object Build()
    {
        var query = _searchQuery.Value.Trim();

        // Build file -> memories mapping
        var fileMemoryMap = new Dictionary<string, List<MemoryNote>>(StringComparer.OrdinalIgnoreCase);
        foreach (var note in _memories)
        {
            foreach (var relPath in note.Targets.Keys)
            {
                var norm = relPath.Replace('\\', '/');
                if (!fileMemoryMap.TryGetValue(norm, out var list))
                {
                    list = new List<MemoryNote>();
                    fileMemoryMap[norm] = list;
                }
                list.Add(note);
            }
        }

        // Combine repository files and target files from memories
        var combinedFiles = new HashSet<string>(_allFiles.Select(f => f.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);
        foreach (var note in _memories)
        {
            foreach (var target in note.Targets.Keys)
            {
                combinedFiles.Add(target.Replace('\\', '/'));
            }
        }

        var fileList = combinedFiles.AsEnumerable();

        if (_onlyLinkedFilter.Value)
        {
            fileList = fileList.Where(f => fileMemoryMap.ContainsKey(f));
        }

        if (!string.IsNullOrEmpty(query))
        {
            fileList = fileList.Where(f => f.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        var sortedFiles = fileList.OrderByDescending(f => fileMemoryMap.ContainsKey(f))
                                  .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        // Build hierarchical node tree
        var treeRoot = BuildFileTree(sortedFiles, fileMemoryMap);
        var treeItems = BuildMenuItems(treeRoot);

        var treeWidget = new Tree(treeItems)
            .OnSelect(e =>
            {
                var selectedPath = e.Value?.ToString();
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    _selectedFile.Set(selectedPath);
                }
            });

        var sourceSelectInput = _projectFilter.ToSelectInput(_projectOptions)
            .Placeholder("All Repos & Promptwares")
            .Nullable()
            .Width(Size.Full());

        var searchInput = _searchQuery.ToTextInput(placeholder: "Search files...")
            .Prefix(Icons.Search)
            .Width(Size.Full());

        var filterToggle = Layout.Horizontal().AlignContent(Align.Left).Gap(1).Width(Size.Full())
            | new Button("All Files").Variant(_onlyLinkedFilter.Value ? ButtonVariant.Outline : ButtonVariant.Primary).Small()
                .OnClick(() => _onlyLinkedFilter.Set(false))
            | new Button("Linked Only").Variant(_onlyLinkedFilter.Value ? ButtonVariant.Primary : ButtonVariant.Outline).Small()
                .OnClick(() => _onlyLinkedFilter.Set(true));

        var topHeaderControls = Layout.Vertical().Gap(2).Width(Size.Full())
            | sourceSelectInput
            | searchInput
            | filterToggle;

        var treeContent = treeItems.Length > 0
            ? (object)(Layout.Vertical().AlignContent(Align.TopLeft).Scroll(Scroll.Auto).Size(Size.Full()) | treeWidget)
            : Layout.Vertical().AlignContent(Align.TopLeft).Padding(4) | Text.Muted("No matching files found.");

        return Layout.Vertical().AlignContent(Align.TopLeft).Gap(2).Padding(2).Size(Size.Full()).RemoveParentPadding()
            | topHeaderControls
            | treeContent;
    }

    private static FileExplorerTreeNode BuildFileTree(IEnumerable<string> filePaths, Dictionary<string, List<MemoryNote>> fileMemoryMap)
    {
        var root = new FileExplorerTreeNode { Name = "" };
        foreach (var path in filePaths)
        {
            var normPath = path.Replace('\\', '/');
            var segments = normPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                if (!current.Folders.TryGetValue(seg, out var child))
                {
                    child = new FileExplorerTreeNode { Name = seg };
                    current.Folders[seg] = child;
                }
                current = child;
            }

            var fileName = segments.Length > 0 ? segments[^1] : normPath;
            var memories = fileMemoryMap.GetValueOrDefault(normPath) ?? new List<MemoryNote>();
            current.Files.Add((normPath, fileName, memories));
        }

        return root;
    }

    private static MenuItem[] BuildMenuItems(FileExplorerTreeNode node)
    {
        var items = new List<MenuItem>();

        foreach (var folder in node.Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var children = BuildMenuItems(folder);
            items.Add(new MenuItem(folder.Name)
                .Icon(Icons.Folder)
                .Children(children)
                .Expanded(true));
        }

        foreach (var file in node.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var isLinked = file.Memories.Count > 0;
            var icon = isLinked ? Icons.FileCheck : Icons.FileCode;
            var color = isLinked ? Colors.Purple : Colors.Slate;

            var item = new MenuItem(file.Name)
                .Icon(icon)
                .Color(color)
                .Tag(file.Path)
                .Tooltip(file.Path);

            if (isLinked)
            {
                item = item with { Badge = $"{file.Memories.Count}" };
            }

            items.Add(item);
        }

        return items.ToArray();
    }
}
