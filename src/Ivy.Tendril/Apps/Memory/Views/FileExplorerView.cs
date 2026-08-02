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

public record ProjectOrPromptwareOption(
    string Id,
    string Label,
    string Path,
    string Name,
    bool IsPromptware
);

public class FileExplorerView : ViewBase
{
    private readonly List<ProjectConfig> _projects;
    private readonly Dictionary<string, PromptwareConfig> _configuredPromptwares;
    private readonly IState<string?> _selectedSourceKey;
    private readonly List<MemoryNote> _memories;
    private readonly IState<string?> _selectedFile;
    private readonly string _workingDir;

    public FileExplorerView(
        List<ProjectConfig> projects,
        Dictionary<string, PromptwareConfig> configuredPromptwares,
        IState<string?> selectedSourceKey,
        List<MemoryNote> memories,
        IState<string?> selectedFile,
        string workingDir)
    {
        _projects = projects;
        _configuredPromptwares = configuredPromptwares;
        _selectedSourceKey = selectedSourceKey;
        _memories = memories;
        _selectedFile = selectedFile;
        _workingDir = workingDir;
    }

    public override object Build()
    {
        var searchQuery = UseState<string>("");
        var onlyLinkedFilter = UseState<bool>(false);

        var sourceOptions = GetAllSourceOptions(_projects, _workingDir, _configuredPromptwares);
        var selectOptions = sourceOptions.Select(o => new Option<string>(o.Id, o.Label)).ToArray<IAnyOption>();

        // Ensure selectedSourceKey is set to a valid option
        if (string.IsNullOrEmpty(_selectedSourceKey.Value) || !sourceOptions.Any(o => o.Id == _selectedSourceKey.Value))
        {
            if (sourceOptions.Count > 0)
            {
                _selectedSourceKey.Set(sourceOptions[0].Id);
            }
        }

        var currentMatch = sourceOptions.FirstOrDefault(o => string.Equals(o.Id, _selectedSourceKey.Value, StringComparison.OrdinalIgnoreCase))
            ?? sourceOptions.FirstOrDefault();

        var targetDir = (currentMatch != null && !string.IsNullOrEmpty(currentMatch.Path) && Directory.Exists(currentMatch.Path))
            ? currentMatch.Path
            : _workingDir;

        // Directly scan files of selected targetDir
        var scannedFiles = new List<string>();
        try
        {
            if (Directory.Exists(targetDir))
            {
                scannedFiles = Directory.GetFiles(targetDir, "*.*", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(targetDir, p).Replace('\\', '/'))
                    .Where(p => !p.StartsWith('.') && !p.Contains("/.") && !p.Contains("bin/") && !p.Contains("obj/") && !p.Contains("node_modules/"))
                    .Take(1000)
                    .ToList();
            }
        }
        catch { }

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

        var fileList = scannedFiles.AsEnumerable();

        if (onlyLinkedFilter.Value)
        {
            fileList = fileList.Where(f => fileMemoryMap.ContainsKey(f));
        }

        var query = searchQuery.Value.Trim();
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

        // Single Select Input for Projects & Promptwares at top of sidebar
        var sourceSelectInput = _selectedSourceKey.ToSelectInput(selectOptions)
            .Placeholder("Select Project or Promptware...")
            .Width(Size.Full())
            .WithField()
            .Label("Project / Promptware");

        var searchInput = searchQuery.ToTextInput(placeholder: "Search files...")
            .Prefix(Icons.Search)
            .Width(Size.Full());

        var filterToggle = Layout.Horizontal().AlignContent(Align.Left).Gap(1).Width(Size.Full())
            | new Button("All Files").Variant(onlyLinkedFilter.Value ? ButtonVariant.Outline : ButtonVariant.Primary).Small()
                .OnClick(() => onlyLinkedFilter.Set(false))
            | new Button("Linked Only").Variant(onlyLinkedFilter.Value ? ButtonVariant.Primary : ButtonVariant.Outline).Small()
                .OnClick(() => onlyLinkedFilter.Set(true));

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

    public static List<ProjectOrPromptwareOption> GetAllSourceOptions(
        List<ProjectConfig> projects,
        string workingDir,
        Dictionary<string, PromptwareConfig>? configuredPromptwares)
    {
        var options = new List<ProjectOrPromptwareOption>();
        var normWorkingDir = workingDir.Replace('\\', '/').TrimEnd('/');

        // 1. Projects
        if (projects != null && projects.Count > 0)
        {
            foreach (var proj in projects)
            {
                if (proj.Repos != null && proj.Repos.Count > 0)
                {
                    foreach (var repo in proj.Repos)
                    {
                        var rawPath = Path.IsPathRooted(repo.Path)
                            ? repo.Path
                            : Path.Combine(workingDir, repo.Path);
                        var fullPath = Path.GetFullPath(rawPath).Replace('\\', '/').TrimEnd('/');
                        var name = Path.GetFileName(fullPath);
                        if (string.IsNullOrEmpty(name)) name = proj.Name;
                        var label = $"📁 Project: {name}";
                        var id = $"proj:{name}:{options.Count}";
                        options.Add(new ProjectOrPromptwareOption(id, label, fullPath, name, false));
                    }
                }
                else
                {
                    var label = $"📁 Project: {proj.Name}";
                    var id = $"proj:{proj.Name}:{options.Count}";
                    options.Add(new ProjectOrPromptwareOption(id, label, normWorkingDir, proj.Name, false));
                }
            }
        }
        else
        {
            var name = Path.GetFileName(normWorkingDir);
            var label = $"📁 Project: {name}";
            var id = $"proj:{name}:{options.Count}";
            options.Add(new ProjectOrPromptwareOption(id, label, normWorkingDir, name, false));
        }

        // 2. Promptwares
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tendril");
        var promptwareRoots = new[]
        {
            Path.Combine(workingDir, "Promptwares"),
            Path.Combine(workingDir, "src", "Ivy.Tendril", "Promptwares"),
            Path.Combine(workingDir, ".tendril", "promptwares"),
            Path.Combine(tendrilHome, "Promptwares"),
            PromptwareHelper.ResolvePromptsRoot(tendrilHome)
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
                    var normPwDir = Path.GetFullPath(pwDir).Replace('\\', '/').TrimEnd('/');
                    var label = $"✨ Promptware: {pwName}";
                    var id = $"pw:{pwName}:{options.Count}";
                    options.Add(new ProjectOrPromptwareOption(id, label, normPwDir, pwName, true));
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
                    var normFolder = Path.GetFullPath(folder).Replace('\\', '/').TrimEnd('/');
                    var label = $"✨ Promptware: {name}";
                    var id = $"pw:{name}:{options.Count}";
                    options.Add(new ProjectOrPromptwareOption(id, label, normFolder, name, true));
                }
            }
        }

        return options;
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
