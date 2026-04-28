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
        var expandedFiles = UseState(new HashSet<string>());
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

        // Build tree items from file list
        var treeItems = fileDiffs.Select(fd =>
        {
            var fileName = Path.GetFileName(fd.FilePath);
            var (icon, color) = PlanContentHelpers.GetFileStatusIconAndColor(fd.Status);
            return new MenuItem(fileName)
                .Icon(icon)
                .Color(color)
                .Tag(fd.FilePath)
                .Tooltip(fd.FilePath);
        }).ToArray();

        var tree = new Tree(treeItems)
            .OnSelect(e =>
            {
                var path = e.Value?.ToString();
                if (path is null) return;
                selectedFile.Set(path);
                // Also expand the file when selected from tree
                if (!expandedFiles.Value.Contains(path))
                {
                    var files = new HashSet<string>(expandedFiles.Value) { path };
                    expandedFiles.Set(files);
                }
            });

        // Stats header
        var statsText =
            $"{changesData.Files.Count} files changed ({changesData.AddedCount} added, {changesData.ModifiedCount} modified, {changesData.DeletedCount} deleted)";

        // Build per-file collapsible diff sections
        var diffsLayout = Layout.Vertical().Gap(2);
        diffsLayout |= Text.Block(statsText).Bold();

        foreach (var fileDiff in fileDiffs)
        {
            var isExpanded = expandedFiles.Value.Contains(fileDiff.FilePath);
            var chevronIcon = isExpanded ? Icons.ChevronDown : Icons.ChevronRight;
            var (statusIcon, statusColor) = PlanContentHelpers.GetFileStatusIconAndColor(fileDiff.Status);
            var fileName = Path.GetFileName(fileDiff.FilePath);

            var header = Layout.Horizontal()
                .Gap(2)
                | new Icon(chevronIcon).Small()
                | new Icon(statusIcon).Small().Color(statusColor)
                | Text.Block(fileName).Bold()
                | Text.Muted(Path.GetDirectoryName(fileDiff.FilePath)?.Replace('\\', '/') ?? "");

            var path = fileDiff.FilePath;
            diffsLayout |= new Box(header)
                .BorderThickness(0).Padding(1)
                .OnClick(() =>
                {
                    var files = new HashSet<string>(expandedFiles.Value);
                    if (!files.Add(path)) files.Remove(path);
                    expandedFiles.Set(files);
                });

            if (isExpanded)
            {
                diffsLayout |= new DiffView().Diff(fileDiff.Diff).Split();
            }
        }

        var sidebarContent = Layout.Vertical().Gap(2).Padding(1)
            | tree;

        return new SidebarLayout(
            mainContent: diffsLayout,
            sidebarContent: sidebarContent,
            width: Size.Rem(16)
        ).Resizable();
    }
}
