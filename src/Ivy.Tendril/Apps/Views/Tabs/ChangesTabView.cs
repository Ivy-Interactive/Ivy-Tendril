using Ivy.Tendril.Helpers;
using Ivy.Widgets.DiffView;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class ChangesTabView(
    PlanContentHelpers.AllChangesData? changesData,
    bool loading,
    Exception? error) : ViewBase
{
    public int FileCount => changesData?.Files.Count ?? 0;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var hideFormatting = UseState(true);

        if (loading && changesData is null)
            return Text.Muted("Loading...");

        if (changesData is null)
        {
            var errorMsg = error is { } err
                ? $"Failed to load changes: {err.Message}"
                : "No commits yet.";
            return Text.Muted(errorMsg);
        }

        var allFileDiffs = PlanContentHelpers.SplitDiffByFile(changesData);

        if (allFileDiffs.Count == 0 && changesData.Files.Count == 0)
            return Text.Muted("No file changes.");

        var fileDiffs = allFileDiffs;
        var hiddenCount = 0;
        if (hideFormatting.Value)
        {
            var beforeCount = allFileDiffs.Count;
            fileDiffs = allFileDiffs.Where(fd => !PlanContentHelpers.IsFormattingOnly(fd)).ToList();
            hiddenCount = beforeCount - fileDiffs.Count;
            Console.WriteLine($"[ChangesTab] Filtering enabled: {beforeCount} total files, {hiddenCount} hidden, {fileDiffs.Count} shown");
        }
        else
        {
            Console.WriteLine($"[ChangesTab] Filtering disabled: showing all {allFileDiffs.Count} files");
        }

        var root = BuildFileTree(fileDiffs);
        var treeItems = ChildItems(root);
        var sortedFileDiffs = SortByTreeOrder(fileDiffs, root);

        var tree = new Tree(treeItems)
            .OnSelect(e =>
            {
                var path = e.Value?.ToString();
                if (path is null) return;
                client.Redirect($"#{path}");
            });

        // var statsText =
        //     $"{changesData.Files.Count} files changed ({changesData.AddedCount} added, {changesData.ModifiedCount} modified, {changesData.DeletedCount} deleted)";

        var diffsLayout = Layout.Vertical().Gap(2).Width(Size.Grow().Min(Size.Px(0))).Scroll(Scroll.Auto).Height(Size.Full().Min(Size.Px(0)));
        //diffsLayout |= Text.Block(statsText).Bold();

        foreach (var fileDiff in sortedFileDiffs)
        {
            var path = fileDiff.FilePath;
            diffsLayout |= Text.Block("").Anchor(path);
            diffsLayout |= new DiffView()
                .Diff(fileDiff.Diff)
                .Split()
                .Collapsible()
                .Width(Size.Full());
        }

        var treePanel = Layout.Vertical().Gap(2).Padding(1)
            .Width(Size.Rem(12).Min(Size.Rem(12))).Scroll(Scroll.Auto).Height(Size.Full().Min(Size.Px(0)))
            | tree;

        var toolbar = Layout.Horizontal().Gap(2).Padding(1)
            | hideFormatting.ToSwitchInput(label: "Hide formatting changes");

        // Show stats: always display total, and show filtered count when applicable
        if (hideFormatting.Value)
        {
            if (hiddenCount > 0)
                toolbar |= Text.Muted($"{fileDiffs.Count} of {allFileDiffs.Count} files (hiding {hiddenCount} formatting-only)").Small();
            else
                toolbar |= Text.Muted($"{allFileDiffs.Count} files (no formatting-only changes to hide)").Small();
        }
        else
        {
            toolbar |= Text.Muted($"Showing all {allFileDiffs.Count} files").Small();
        }

        var mainLayout = Layout.Horizontal().Height(Size.Full().Min(Size.Px(0)))
            | treePanel
            | diffsLayout;

        return Layout.Vertical().Height(Size.Full().Min(Size.Px(0)))
            | toolbar
            | mainLayout;
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
