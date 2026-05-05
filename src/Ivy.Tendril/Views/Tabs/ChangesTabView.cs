using Ivy.Tendril.Helpers;
using Ivy.Widgets.DiffView;

namespace Ivy.Tendril.Views.Tabs;

public class ChangesTabView(
    PlanContentHelpers.AllChangesData? changesData,
    bool loading,
    Exception? error) : ViewBase
{
    public int FileCount => changesData?.Files.Count ?? 0;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        
        // null sentinel = "user hasn't toggled yet, default to all expanded"
        var expandedFiles = UseState<HashSet<string>?>(() => null);
        var selectedFile = UseState<string?>(null);

        if (loading)
            return Text.Muted("Loading...");

        if (changesData is null)
        {
            var errorMsg = error is { } err
                ? $"Failed to load changes: {err.Message}"
                : "No commits yet.";
            return Text.Muted(errorMsg);
        }

        var fileDiffs = PlanContentHelpers.SplitDiffByFile(changesData);

        if (fileDiffs.Count == 0 && changesData.Files.Count == 0)
            return Text.Muted("No file changes.");

        var currentlyExpanded = expandedFiles.Value
            ?? new HashSet<string>(fileDiffs.Select(fd => fd.FilePath));

        var root = BuildFileTree(fileDiffs);
        var treeItems = ChildItems(root);
        var sortedFileDiffs = SortByTreeOrder(fileDiffs, root);

        var tree = new Tree(treeItems)
            .OnSelect(e =>
            {
                var path = e.Value?.ToString();
                if (path is null) return;
                selectedFile.Set(path);
                if (!currentlyExpanded.Contains(path))
                {
                    var files = new HashSet<string>(currentlyExpanded) { path };
                    expandedFiles.Set(files);
                }
                client.Redirect($"#{path}");
            });

        var statsText =
            $"{changesData.Files.Count} files changed ({changesData.AddedCount} added, {changesData.ModifiedCount} modified, {changesData.DeletedCount} deleted)";

        var diffsLayout = Layout.Vertical().Gap(2).Width(Size.Grow().Min(Size.Px(0))).Scroll(Scroll.Auto).Height(Size.Full());
        diffsLayout |= Text.Block(statsText).Bold();

        foreach (var fileDiff in sortedFileDiffs)
        {
            var isExpanded = currentlyExpanded.Contains(fileDiff.FilePath);
            var chevronIcon = isExpanded ? Icons.ChevronDown : Icons.ChevronRight;
            var (statusIcon, statusColor) = PlanContentHelpers.GetFileStatusIconAndColor(fileDiff.Status);
            var fileName = Path.GetFileName(fileDiff.FilePath);
            var isRenamed = fileDiff.OldFilePath != null;
            var oldFileName = isRenamed ? Path.GetFileName(fileDiff.OldFilePath!) : null;

            var header = Layout.Horizontal()
                .Gap(2)
                | new Icon(chevronIcon).Small()
                | new Icon(statusIcon).Small().Color(statusColor);

            if (isRenamed)
            {
                header |= Text.Block(oldFileName!).Bold();
                header |= Text.Muted("→");
                header |= Text.Block(fileName).Bold();
                header |= Text.Muted(Path.GetDirectoryName(fileDiff.FilePath)?.Replace('\\', '/') ?? "");
            }
            else
            {
                header |= Text.Block(fileName).Bold();
                header |= Text.Muted(Path.GetDirectoryName(fileDiff.FilePath)?.Replace('\\', '/') ?? "");
            }

            var path = fileDiff.FilePath;
            
            diffsLayout |= Text.Block("").Anchor(path);

            diffsLayout |= new Box(header)
                .BorderThickness(0).Padding(1)
                .Hover(HoverEffect.Pointer)
                .OnClick(() =>
                {
                    var files = new HashSet<string>(currentlyExpanded);
                    if (!files.Add(path)) files.Remove(path);
                    expandedFiles.Set(files);
                });

            if (isExpanded)
            {
                diffsLayout |= new DiffView().Diff(fileDiff.Diff).Split().Width(Size.Full());
            }
        }

        var treePanel = Layout.Vertical().Gap(2).Padding(1)
            .Width(Size.Rem(16).Min(Size.Rem(16))).Scroll(Scroll.Auto).Height(Size.Full())
            | tree;

        return Layout.Horizontal().Height(Size.Full())
            | treePanel
            | diffsLayout;
    }

    private static TreeNode BuildFileTree(IReadOnlyList<PlanContentHelpers.FileDiff> fileDiffs)
    {
        var root = new TreeNode("");
        foreach (var fd in fileDiffs)
        {
            var segments = fd.FilePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                if (!node.Folders.TryGetValue(seg, out var child))
                {
                    child = new TreeNode(seg);
                    node.Folders[seg] = child;
                }
                node = child;
            }
            node.Files.Add(fd);
        }
        return root;
    }

    private static MenuItem[] ChildItems(TreeNode node)
    {
        var items = new List<MenuItem>();
        foreach (var folder in node.Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(FolderItem(folder));
        }
        foreach (var file in node.Files.OrderBy(f => Path.GetFileName(f.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            var (icon, color) = PlanContentHelpers.GetFileStatusIconAndColor(file.Status);
            items.Add(new MenuItem(Path.GetFileName(file.FilePath))
                .Icon(icon)
                .Color(color)
                .Tag(file.FilePath)
                .Tooltip(file.FilePath));
        }
        return items.ToArray();
    }

    private static List<string> FlattenTreeOrder(TreeNode node)
    {
        var result = new List<string>();
        FlattenTreeOrderRecursive(node, result);
        return result;
    }

    private static void FlattenTreeOrderRecursive(TreeNode node, List<string> result)
    {
        foreach (var folder in node.Folders.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            FlattenTreeOrderRecursive(folder, result);
        foreach (var file in node.Files.OrderBy(f => Path.GetFileName(f.FilePath), StringComparer.OrdinalIgnoreCase))
            result.Add(file.FilePath);
    }

    private static List<PlanContentHelpers.FileDiff> SortByTreeOrder(
        IReadOnlyList<PlanContentHelpers.FileDiff> fileDiffs, TreeNode root)
    {
        var orderedPaths = FlattenTreeOrder(root);
        var lookup = fileDiffs.ToDictionary(fd => fd.FilePath);
        return orderedPaths
            .Where(lookup.ContainsKey)
            .Select(p => lookup[p])
            .ToList();
    }

    // Collapse single-child folder chains (e.g. "src/components" if src has only the components folder)
    // for a more compact GitHub-style tree.
    private static MenuItem FolderItem(TreeNode node)
    {
        var label = node.Name;
        while (node.Files.Count == 0 && node.Folders.Count == 1)
        {
            var only = node.Folders.Values.First();
            label = $"{label}/{only.Name}";
            node = only;
        }

        var item = new MenuItem(label, ChildItems(node)).Icon(Icons.Folder).Expanded();
        var folderColor = GetFolderColor(node);
        return folderColor is not null ? item.Color(folderColor.Value) : item;
    }

    private static Colors? GetFolderColor(TreeNode node)
    {
        var hasAdded = false;
        var hasDeleted = false;
        var hasOther = false;
        CollectStatuses(node);
        if (!hasAdded && !hasDeleted && !hasOther) return null;
        if (hasAdded && !hasDeleted && !hasOther) return Colors.Success;
        if (hasDeleted && !hasAdded && !hasOther) return Colors.Destructive;
        return Colors.Neutral;

        void CollectStatuses(TreeNode n)
        {
            foreach (var f in n.Files)
            {
                switch (f.Status)
                {
                    case "A": hasAdded = true; break;
                    case "D": hasDeleted = true; break;
                    default: hasOther = true; break;
                }
                if (hasAdded && hasDeleted) return;
            }
            foreach (var folder in n.Folders.Values)
                CollectStatuses(folder);
        }
    }

    private sealed class TreeNode(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, TreeNode> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<PlanContentHelpers.FileDiff> Files { get; } = new();
    }
}
