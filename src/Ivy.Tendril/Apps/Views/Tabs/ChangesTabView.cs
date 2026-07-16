using Ivy.Tendril.Helpers;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Apps.Review.Dialogs;

namespace Ivy.Tendril.Apps.Views.Tabs;

public class ChangesTabView(
    PlanContentHelpers.AllChangesData? changesData,
    bool loading,
    Exception? error,
    IState<List<DraftComment>> draftComments,
    PlanFile selectedPlan,
    IJobService jobService,
    Action refreshPlans,
    string? projectName = null) : ViewBase
{
    public int FileCount => changesData?.Files.Count ?? 0;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var planService = UseService<IPlanReaderService>();
        var hideFormatting = UseState(true);

        var (submitReviewDialog, showSubmitReviewDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new SubmitReviewDialog(
                isOpen,
                selectedPlan,
                draftComments.Value,
                draftComments,
                jobService,
                planService,
                refreshPlans
            );
        });

        if (loading && changesData is null)
            return Text.Muted("Loading...");

        if (changesData is null)
        {
            var errorMsg = error is { } err
                ? $"Failed to load changes: {err.Message}"
                : "No commits yet.";
            return Text.Muted(errorMsg);
        }

        object? mismatchBanner = null;
        if (changesData.FromUnlistedWorktree)
        {
            var repoLabel = string.IsNullOrEmpty(changesData.SourceRepoPath)
                ? "a different repository"
                : Path.GetFileName(changesData.SourceRepoPath!.TrimEnd('/', '\\'));
            var projectLabel = string.IsNullOrEmpty(projectName) ? "this plan's project" : $"project '{projectName}'";
            mismatchBanner = Callout.Warning(
                $"These changes are in {repoLabel}, which is not part of {projectLabel}. " +
                "The plan may have been created in the wrong project.", "Wrong project?");
        }

        var allFileDiffs = PlanContentHelpers.SplitDiffByFile(changesData);

        if (allFileDiffs.Count == 0 && changesData.Files.Count == 0)
            return Text.Muted("No file changes.");

        var fileDiffs = allFileDiffs;
        var hiddenCount = 0;
        if (hideFormatting.Value)
        {
            fileDiffs = allFileDiffs.Where(fd => !PlanContentHelpers.IsFormattingOnly(fd)).ToList();
            hiddenCount = allFileDiffs.Count - fileDiffs.Count;
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

        var diffsLayout = Layout.Vertical().Width(Size.Grow().Min(Size.Px(0))).Scroll(Scroll.Auto).Height(Size.Full().Min(Size.Px(0)));

        foreach (var fileDiff in sortedFileDiffs)
        {
            var path = fileDiff.FilePath;
            diffsLayout |= Text.Block("").Anchor(path);
            diffsLayout |= new PlanDiffView
            {
                Diff = fileDiff.Diff,
                FilePath = path,
                Comments = draftComments.Value.Where(c => c.FilePath == path).ToList(),
                OnAddComment = e => {
                    var list = new List<DraftComment>(draftComments.Value);
                    list.Add(e.Value);
                    draftComments.Set(list);
                    return ValueTask.CompletedTask;
                },
                OnUpdateComment = e => {
                    var c = e.Value;
                    var list = new List<DraftComment>(draftComments.Value);
                    var idx = list.FindIndex(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                    if (idx >= 0) {
                        list[idx] = c;
                        draftComments.Set(list);
                    }
                    return ValueTask.CompletedTask;
                },
                OnDeleteComment = e => {
                    var c = e.Value;
                    var list = new List<DraftComment>(draftComments.Value);
                    list.RemoveAll(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                    draftComments.Set(list);
                    return ValueTask.CompletedTask;
                },
                Collapsible = true
            }.Width(Size.Full());
        }

        var treePanel = new Box(Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Full().Min(Size.Px(0))) | tree)
            .Width(Size.Auto()).Height(Size.Full().Min(Size.Px(0)))
            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var mobileFilePicker = MobileItemPicker.Build(
                $"Jump to file ({sortedFileDiffs.Count})",
                sortedFileDiffs,
                fd => fd.FilePath,
                _ => false,
                fd => client.Redirect($"#{fd.FilePath}"))
            .ShowOn(Breakpoint.Mobile, Breakpoint.Tablet);

        var toolbar = Layout.Horizontal().AlignContent(Align.Left).Height(Size.Auto())
            | hideFormatting.ToSwitchInput(label: "Hide formatting changes");

        if (hideFormatting.Value && hiddenCount > 0)
            toolbar |= Text.Muted($"{fileDiffs.Count} of {allFileDiffs.Count} files (hiding {hiddenCount} formatting-only)").Small();

        var draftCount = draftComments.Value.Count;
        var submitBtn = new Button(draftCount > 0 ? $"Submit review ({draftCount})" : "Submit review")
            .Icon(Icons.GitPullRequest)
            .OnClick(() => showSubmitReviewDialog());

        submitBtn = draftCount > 0 ? submitBtn.Primary() : submitBtn.Outline();

        toolbar |= submitBtn;

        var mainLayout = Layout.Horizontal().Height(Size.Full().Min(Size.Px(0)))
            | treePanel
            | diffsLayout;

        var outer = Layout.Vertical().Height(Size.Full().Min(Size.Px(0)));
        if (mismatchBanner != null)
            outer |= mismatchBanner;
        outer |= toolbar;
        outer |= mobileFilePicker;
        outer |= mainLayout;
        outer |= submitReviewDialog;
        return outer;
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
