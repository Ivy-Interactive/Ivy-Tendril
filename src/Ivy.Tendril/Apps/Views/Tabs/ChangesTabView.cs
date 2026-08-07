using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
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
        var gitService = UseService<IGitService>();
        var config = UseService<IConfigService>();
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

        var diffsLayout = Layout.Vertical().Gap(1).Width(Size.Grow().Min(Size.Px(0))).Scroll(Scroll.Auto).Height(Size.Full().Min(Size.Px(0)));
        var isManyFiles = sortedFileDiffs.Count > 10;
        for (var i = 0; i < sortedFileDiffs.Count; i++)
        {
            var fileDiff = sortedFileDiffs[i];
            var path = fileDiff.FilePath;
            diffsLayout |= new PlanDiffView
            {
                Diff = fileDiff.Diff,
                FilePath = path,
                Collapsible = true,
                DefaultCollapsed = isManyFiles && i >= 5,
                Comments = draftComments.Value.Where(c => c.FilePath == path).ToList(),
                OnAddComment = e =>
                {
                    var list = new List<DraftComment>(draftComments.Value);
                    list.Add(e.Value);
                    draftComments.Set(list);
                    return ValueTask.CompletedTask;
                },
                OnUpdateComment = e =>
                {
                    var c = e.Value;
                    var list = new List<DraftComment>(draftComments.Value);
                    var idx = list.FindIndex(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                    if (idx >= 0)
                    {
                        list[idx] = c;
                        draftComments.Set(list);
                    }
                    return ValueTask.CompletedTask;
                },
                OnDeleteComment = e =>
                {
                    var c = e.Value;
                    var list = new List<DraftComment>(draftComments.Value);
                    list.RemoveAll(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                    draftComments.Set(list);
                    return ValueTask.CompletedTask;
                },
                OnDirectEdit = async e =>
                {
                    await HandleDirectEdit(e.Value);
                },
                OnEditFile = e =>
                {
                    var repoPath = changesData?.SourceRepoPath;
                    if (string.IsNullOrEmpty(repoPath))
                    {
                        repoPath = selectedPlan.GetEffectiveRepoPaths(config).FirstOrDefault();
                    }
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var absolutePath = Path.Combine(repoPath, e.Value).Replace('\\', '/');
                        var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "code",
                            Arguments = $"\"{absolutePath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = true
                        });
                    }
                    return ValueTask.CompletedTask;
                },
                OnDeleteFile = async e =>
                {
                    var repoPath = changesData?.SourceRepoPath;
                    if (string.IsNullOrEmpty(repoPath))
                    {
                        repoPath = selectedPlan.GetEffectiveRepoPaths(config).FirstOrDefault();
                    }
                    if (!string.IsNullOrEmpty(repoPath))
                    {
                        var absolutePath = Path.Combine(repoPath, e.Value).Replace('\\', '/');
                        try
                        {
                            if (File.Exists(absolutePath))
                            {
                                File.Delete(absolutePath);
                            }
                            client.Toast($"Deleted file: {e.Value}", "File Deleted");
                            refreshPlans();
                        }
                        catch (Exception ex)
                        {
                            client.Toast($"Failed to delete file: {ex.Message}", "Delete Failed", variant: ToastVariant.Destructive);
                        }
                    }
                    await Task.CompletedTask;
                }
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

        var leftSide = Layout.Horizontal().Gap(2).AlignContent(Align.Left)
            | hideFormatting.ToSwitchInput(label: "Hide formatting changes");

        if (hideFormatting.Value && hiddenCount > 0)
            leftSide |= Text.Muted($"{fileDiffs.Count} of {allFileDiffs.Count} files (hiding {hiddenCount} formatting-only)").Small();

        var totals = PlanContentHelpers.CountDiffLines(fileDiffs);
        var totalsText = Text.Rich().NoWrap().Small()
            .Run($"+{totals.Additions}", color: Colors.Success)
            .Run($" -{totals.Deletions}", color: Colors.Destructive);

        var draftCount = draftComments.Value.Count;
        var submitBtn = new Button(draftCount > 0 ? $"Agent Review ({draftCount})" : "Agent Review")
            .Icon(Icons.GitPullRequest)
            .OnClick(() => showSubmitReviewDialog());

        submitBtn = draftCount > 0 ? submitBtn.Primary() : submitBtn.Outline();

        var rightSide = Layout.Horizontal().Gap(2).AlignContent(Align.Right).Padding(0, 0, 2, 0)
            | totalsText
            | submitBtn;

        var toolbar = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween).Height(Size.Auto()).Padding(2, 0, 0, 0)
            | leftSide
            | rightSide;

        // Padding order is (left, top, right, bottom). Left 2 aligns the tree/diff content with
        // the toolbar above; bottom 4 matches Cap()'s bottom inset so content doesn't run into
        // the action bar separator below.
        var mainLayout = Layout.Horizontal().Height(Size.Full().Min(Size.Px(0))).Padding(2, 0, 2, 4)
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

        async Task HandleDirectEdit(DirectEditArgs args)
        {
            var repoPath = changesData?.SourceRepoPath;
            if (string.IsNullOrEmpty(repoPath))
            {
                var repos = selectedPlan.GetEffectiveRepoPaths(config);
                repoPath = repos.FirstOrDefault();
            }

            if (string.IsNullOrEmpty(repoPath))
            {
                client.Toast("Could not find repository path for direct edit.", "Edit Failed", variant: ToastVariant.Destructive);
                return;
            }

            var absoluteFilePath = Path.Combine(repoPath, args.FilePath);
            if (!File.Exists(absoluteFilePath))
            {
                client.Toast($"File not found at {absoluteFilePath}", "Edit Failed", variant: ToastVariant.Destructive);
                return;
            }

            try
            {
                var lines = await File.ReadAllLinesAsync(absoluteFilePath);
                if (args.LineNumber <= 0 || args.LineNumber > lines.Length)
                {
                    client.Toast($"Invalid line number: {args.LineNumber}. File has {lines.Length} lines.", "Edit Failed", variant: ToastVariant.Destructive);
                    return;
                }

                lines[args.LineNumber - 1] = args.NewContent;
                await File.WriteAllLinesAsync(absoluteFilePath, lines);

                var gitAddResult = RunGitCommand(repoPath, $"add \"{args.FilePath}\"");
                if (gitAddResult.ExitCode == 0)
                {
                    var commitMsg = string.IsNullOrWhiteSpace(args.CommitMessage)
                        ? $"Direct edit: update {Path.GetFileName(args.FilePath)} at line {args.LineNumber}"
                        : args.CommitMessage;
                    var escapedMsg = commitMsg.Replace("\"", "\\\"");
                    var gitCommitResult = RunGitCommand(repoPath, $"commit -m \"{escapedMsg}\"");
                    if (gitCommitResult.ExitCode == 0)
                    {
                        client.Toast($"Successfully edited and committed line {args.LineNumber}.", "Edit Saved");
                    }
                    else
                    {
                        client.Toast($"Edited file on disk, but git commit failed: {gitCommitResult.Output}", "Edit Saved (No Commit)", variant: ToastVariant.Warning);
                    }
                }
                else
                {
                    client.Toast($"Edited file on disk, but git add failed: {gitAddResult.Output}", "Edit Saved (No Commit)", variant: ToastVariant.Warning);
                }

                refreshPlans();
            }
            catch (Exception ex)
            {
                client.Toast($"Failed to write changes: {ex.Message}", "Edit Failed", variant: ToastVariant.Destructive);
            }
        }
    }

    private static (int ExitCode, string Output) RunGitCommand(string repoPath, string args)
    {
        var psi = GitHelper.MakeGitStartInfo(args, repoPath);
        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10000); // 10s timeout
        return (process.ExitCode, output);
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
