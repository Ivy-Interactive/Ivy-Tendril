using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Memory.Views;

public class FileExplorerTreeNode
{
    public string Name { get; set; } = "";
    public Dictionary<string, FileExplorerTreeNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string Path, string Name, List<MemoryNote> Memories)> Files { get; } = new();
}

public record RepoOrPromptwareItem(string Name, string Path, bool IsPromptware);

public class FileExplorerView : ViewBase
{
    private readonly List<ProjectConfig> _projects;
    private readonly Dictionary<string, PromptwareConfig> _configuredPromptwares;
    private readonly IState<string?> _selectedProject;
    private readonly IState<string?> _selectedFolderPath;
    private readonly IState<string?> _selectedFolderName;
    private readonly List<string> _allFiles;
    private readonly List<MemoryNote> _memories;
    private readonly IState<string?> _selectedFile;
    private readonly IState<string> _searchQuery;
    private readonly IState<bool> _onlyLinkedFilter;
    private readonly IState<string?> _projectFilter;
    private readonly string _workingDir;

    public FileExplorerView(
        List<ProjectConfig> projects,
        Dictionary<string, PromptwareConfig> configuredPromptwares,
        IState<string?> selectedProject,
        IState<string?> selectedFolderPath,
        IState<string?> selectedFolderName,
        List<string> allFiles,
        List<MemoryNote> memories,
        IState<string?> selectedFile,
        IState<string> searchQuery,
        IState<bool> onlyLinkedFilter,
        IState<string?> projectFilter,
        string workingDir)
    {
        _projects = projects;
        _configuredPromptwares = configuredPromptwares;
        _selectedProject = selectedProject;
        _selectedFolderPath = selectedFolderPath;
        _selectedFolderName = selectedFolderName;
        _allFiles = allFiles;
        _memories = memories;
        _selectedFile = selectedFile;
        _searchQuery = searchQuery;
        _onlyLinkedFilter = onlyLinkedFilter;
        _projectFilter = projectFilter;
        _workingDir = workingDir;
    }

    public override object Build()
    {
        var query = _searchQuery.Value.Trim();

        // Ensure a project is selected
        if (string.IsNullOrEmpty(_selectedProject.Value) && _projects.Count > 0)
        {
            _selectedProject.Set(_projects.First().Name);
        }

        var projectOptions = _projects.Select(p => new Option<string>(p.Name, p.Name)).ToArray<IAnyOption>();
        var selectedProj = _projects.FirstOrDefault(p => p.Name.Equals(_selectedProject.Value, StringComparison.OrdinalIgnoreCase))
            ?? _projects.FirstOrDefault();

        var repoOrPromptwareItems = GetItemsForProject(selectedProj, _workingDir, _configuredPromptwares);

        // Ensure default selected folder if not set
        if (string.IsNullOrEmpty(_selectedFolderPath.Value) && repoOrPromptwareItems.Count > 0)
        {
            var firstItem = repoOrPromptwareItems.First();
            _selectedFolderPath.Set(firstItem.Path);
            _selectedFolderName.Set(firstItem.Name);
        }

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

        // Combine files in selected folder
        var combinedFiles = new HashSet<string>(_allFiles.Select(f => f.Replace('\\', '/')), StringComparer.OrdinalIgnoreCase);

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

        // Build hierarchical file tree
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

        // Project selector
        var projectSelectInput = _selectedProject.ToSelectInput(projectOptions)
            .Placeholder("Select Project...")
            .WithField()
            .Label("Project")
            .Width(Size.Full());

        // Repositories & Promptwares list
        var repoItems = repoOrPromptwareItems.Where(i => !i.IsPromptware).ToList();
        var promptwareItems = repoOrPromptwareItems.Where(i => i.IsPromptware).ToList();

        var reposListLayout = Layout.Vertical().Gap(1).Width(Size.Full());
        if (repoItems.Count > 0)
        {
            reposListLayout |= Text.Block("Repositories").Small().Bold().Muted();
            foreach (var item in repoItems)
            {
                var isSelected = string.Equals(_selectedFolderPath.Value, item.Path, StringComparison.OrdinalIgnoreCase);
                var btn = new Button(item.Name)
                    .Icon(Icons.FolderGit2)
                    .Variant(isSelected ? ButtonVariant.Primary : ButtonVariant.Ghost)
                    .Small()
                    .Width(Size.Full())
                    .OnClick(() =>
                    {
                        _selectedFolderPath.Set(item.Path);
                        _selectedFolderName.Set(item.Name);
                        _projectFilter.Set(item.Name);
                    });
                reposListLayout |= btn;
            }
        }

        var promptwaresListLayout = Layout.Vertical().Gap(1).Width(Size.Full());
        if (promptwareItems.Count > 0)
        {
            promptwaresListLayout |= Text.Block("Promptwares").Small().Bold().Muted();
            foreach (var item in promptwareItems)
            {
                var isSelected = string.Equals(_selectedFolderPath.Value, item.Path, StringComparison.OrdinalIgnoreCase);
                var btn = new Button(item.Name)
                    .Icon(Icons.Sparkles)
                    .Variant(isSelected ? ButtonVariant.Primary : ButtonVariant.Ghost)
                    .Small()
                    .Width(Size.Full())
                    .OnClick(() =>
                    {
                        _selectedFolderPath.Set(item.Path);
                        _selectedFolderName.Set(item.Name);
                        _projectFilter.Set(item.Name);
                    });
                promptwaresListLayout |= btn;
            }
        }

        var searchInput = _searchQuery.ToTextInput(placeholder: "Search files...")
            .Prefix(Icons.Search)
            .Width(Size.Full());

        var filterToggle = Layout.Horizontal().AlignContent(Align.Left).Gap(1).Width(Size.Full())
            | new Button("All Files").Variant(_onlyLinkedFilter.Value ? ButtonVariant.Outline : ButtonVariant.Primary).Small()
                .OnClick(() => _onlyLinkedFilter.Set(false))
            | new Button("Linked Only").Variant(_onlyLinkedFilter.Value ? ButtonVariant.Primary : ButtonVariant.Outline).Small()
                .OnClick(() => _onlyLinkedFilter.Set(true));

        var folderHeader = Text.Block($"Files in {_selectedFolderName.Value ?? "Folder"}").Bold().Small();

        var topHeaderControls = Layout.Vertical().Gap(2).Width(Size.Full())
            | projectSelectInput
            | reposListLayout
            | promptwaresListLayout
            | new Spacer().Height(Size.Units(1))
            | folderHeader
            | searchInput
            | filterToggle;

        var treeContent = treeItems.Length > 0
            ? (object)(Layout.Vertical().AlignContent(Align.TopLeft).Scroll(Scroll.Auto).Size(Size.Full()) | treeWidget)
            : Layout.Vertical().AlignContent(Align.TopLeft).Padding(4) | Text.Muted("No matching files found.");

        return Layout.Vertical().AlignContent(Align.TopLeft).Gap(2).Padding(2).Size(Size.Full()).RemoveParentPadding()
            | topHeaderControls
            | treeContent;
    }

    public static List<RepoOrPromptwareItem> GetItemsForProject(ProjectConfig? project, string workingDir, Dictionary<string, PromptwareConfig>? configuredPromptwares)
    {
        var items = new List<RepoOrPromptwareItem>();
        if (project == null)
        {
            items.Add(new RepoOrPromptwareItem("Workspace", workingDir, false));
            return items;
        }

        // Add repositories
        foreach (var repo in project.Repos)
        {
            var fullPath = Path.IsPathRooted(repo.Path)
                ? repo.Path
                : Path.GetFullPath(Path.Combine(workingDir, repo.Path));
            var name = Path.GetFileName(fullPath.TrimEnd('/', '\\'));
            if (string.IsNullOrEmpty(name)) name = fullPath;
            items.Add(new RepoOrPromptwareItem(name, fullPath, false));
        }

        if (items.Count == 0)
        {
            items.Add(new RepoOrPromptwareItem(project.Name, workingDir, false));
        }

        // Add promptwares
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
        var promptwareRoots = new[]
        {
            Path.Combine(workingDir, "Promptwares"),
            Path.Combine(workingDir, ".tendril", "promptwares"),
            Path.Combine(tendrilHome, "Promptwares"),
            Path.Combine(Path.GetDirectoryName(tendrilHome) ?? "", ".local", "share", "tendril", "promptwares")
        };

        var seenPw = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pwRoot in promptwareRoots)
        {
            if (!Directory.Exists(pwRoot)) continue;
            foreach (var pwDir in Directory.GetDirectories(pwRoot))
            {
                var pwName = Path.GetFileName(pwDir);
                if (pwName.Equals("memories", StringComparison.OrdinalIgnoreCase)) continue;
                if (seenPw.Add(pwName))
                {
                    items.Add(new RepoOrPromptwareItem(pwName, pwDir, true));
                }
            }
        }

        if (configuredPromptwares != null)
        {
            foreach (var (name, pwConfig) in configuredPromptwares)
            {
                if (seenPw.Add(name))
                {
                    var folder = PromptwareHelper.ResolvePromptwareFolder(name, tendrilHome);
                    items.Add(new RepoOrPromptwareItem(name, folder, true));
                }
            }
        }

        return items;
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
