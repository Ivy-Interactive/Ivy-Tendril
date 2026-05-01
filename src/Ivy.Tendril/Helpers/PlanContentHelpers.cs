using System.Text.RegularExpressions;
using Ivy.Tendril.Models;
using Ivy.Widgets.DiffView;

using Ivy.Tendril.Services;
namespace Ivy.Tendril.Helpers;

public static class PlanContentHelpers
{
    public record CommitRow(string Hash, string ShortHash, string Title, int? FileCount);

    public record FileDiff(string FilePath, string Status, string Diff, string? OldFilePath = null);

    public static List<FileDiff> SplitDiffByFile(AllChangesData changesData)
    {
        var result = new List<FileDiff>();
        if (string.IsNullOrWhiteSpace(changesData.Diff))
            return result;

        // Split on "diff --git " boundaries
        var chunks = Regex.Split(changesData.Diff, @"(?=^diff --git )", RegexOptions.Multiline);

        // Build a lookup from file path to status
        var statusLookup = new Dictionary<string, string>();
        foreach (var (status, filePath) in changesData.Files)
        {
            statusLookup[filePath] = status;
        }

        foreach (var chunk in chunks)
        {
            if (string.IsNullOrWhiteSpace(chunk) || !chunk.StartsWith("diff --git "))
                continue;

            // Extract file path from "diff --git a/path b/path"
            var headerMatch = Regex.Match(chunk, @"^diff --git a/(.+?) b/(.+?)$", RegexOptions.Multiline);
            if (!headerMatch.Success)
                continue;

            var filePath = headerMatch.Groups[2].Value;
            var oldFilePath = headerMatch.Groups[1].Value;
            var status = statusLookup.GetValueOrDefault(filePath, "M");
            var oldPath = oldFilePath != filePath ? oldFilePath : null;
            result.Add(new FileDiff(filePath, status, chunk.TrimEnd(), oldPath));
        }

        return result;
    }

    public record CommitDetailData(
        string Title,
        string? Diff,
        List<(string Status, string FilePath)>? Files
    );

    public static Dictionary<string, List<string>> GetArtifacts(string folderPath)
    {
        var artifactsDir = Path.Combine(folderPath, "artifacts");
        var result = new Dictionary<string, List<string>>();
        if (!Directory.Exists(artifactsDir)) return result;

        foreach (var subDir in Directory.GetDirectories(artifactsDir))
        {
            var category = Path.GetFileName(subDir);
            var files = Directory.GetFiles(subDir, "*", SearchOption.AllDirectories).ToList();
            if (files.Count > 0)
                result[category] = files;
        }

        var rootFiles = Directory.GetFiles(artifactsDir).ToList();
        if (rootFiles.Count > 0)
            result["other"] = rootFiles;

        return result;
    }

    public static List<CommitRow> BuildCommitRows(PlanFile plan, IConfigService config, IGitService gitService)
    {
        if (plan.Commits.Count == 0) return [];

        var repoPaths = plan.GetEffectiveRepoPaths(config);

        // Try batch lookup: single git process for all commits per repo
        foreach (var repo in repoPaths)
        {
            var summariesResult = gitService.GetCommitSummaries(repo, plan.Commits);
            if (!summariesResult.IsSuccess || summariesResult.Value == null || summariesResult.Value.Count == 0) continue;

            return plan.Commits.Select(commit =>
            {
                var shortHash = commit.Length > 7 ? commit[..7] : commit;
                if (summariesResult.Value.TryGetValue(commit, out var info))
                    return new CommitRow(commit, shortHash, info.Title, info.FileCount);
                return new CommitRow(commit, shortHash, "", null);
            }).ToList();
        }

        // Fallback: individual lookups if batch fails
        return plan.Commits.Select(commit =>
        {
            string? title = null;
            int? fileCount = null;
            foreach (var repo in repoPaths)
            {
                var titleResult = gitService.GetCommitTitle(repo, commit);
                if (titleResult.IsSuccess)
                {
                    title = titleResult.Value;
                    var fileCountResult = gitService.GetCommitFileCount(repo, commit);
                    fileCount = fileCountResult.IsSuccess ? fileCountResult.Value : null;
                    break;
                }
            }
            var shortHash = commit.Length > 7 ? commit[..7] : commit;
            return new CommitRow(commit, shortHash, title ?? "", fileCount);
        }).ToList();
    }

    public static object? BuildCommitWarningCallout(List<CommitRow> commitRows)
    {
        var issues = new List<string>();
        foreach (var row in commitRows)
        {
            if (string.IsNullOrWhiteSpace(row.Title))
                issues.Add($"Commit `{row.ShortHash}` has no message");
            if (row.FileCount == 0)
                issues.Add($"Commit `{row.ShortHash}` has no file changes");
        }

        if (issues.Count == 0)
            return null;

        var message = string.Join("\n\n", issues);
        return Callout.Warning(message, "Commit Warning");
    }

    public static object RenderCommitDetailSheet(CommitDetailData? data, bool loading, string? commitHash,
        Action closeSheet, HashSet<string>? expandedFiles = null, Action<string>? onToggleFile = null,
        Exception? error = null)
    {
        if (commitHash is null) return new Empty();

        var shortHash = commitHash.Length > 7 ? commitHash[..7] : commitHash;
        object sheetContent;

        if (loading)
        {
            sheetContent = Text.Muted("Loading...");
        }
        else if (error is not null)
        {
            sheetContent = Text.Muted($"Failed to load commit: {error.Message}");
        }
        else if (data is null)
        {
            sheetContent = Text.Muted("Commit not found.");
        }
        else
        {
            var commitSheetContent = Layout.Vertical().Gap(4).Padding(2);

            if (!string.IsNullOrWhiteSpace(data.Diff) && data.Files is { Count: > 0 })
            {
                // Build AllChangesData to reuse SplitDiffByFile
                var changesData = new AllChangesData(data.Diff, data.Files, 0, 0, 0);
                var fileDiffs = SplitDiffByFile(changesData);

                foreach (var fileDiff in fileDiffs)
                {
                    var isExpanded = expandedFiles?.Contains(fileDiff.FilePath) ?? false;
                    var chevronIcon = isExpanded ? Icons.ChevronDown : Icons.ChevronRight;
                    var (statusIcon, statusColor) = GetFileStatusIconAndColor(fileDiff.Status);
                    var fileName = Path.GetFileName(fileDiff.FilePath);
                    var isRenamed = fileDiff.OldFilePath != null;
                    var oldFileName = isRenamed ? Path.GetFileName(fileDiff.OldFilePath!) : null;

                    var header = Layout.Horizontal().Gap(2)
                        | new Icon(chevronIcon).Small()
                        | new Icon(statusIcon).Small().Color(statusColor);

                    if (isRenamed)
                    {
                        header |= Text.Block(oldFileName!).Bold();
                        header |= Text.Muted("→");
                        header |= Text.Block(fileName).Bold();
                        header |= Text.Muted(fileDiff.FilePath);
                    }
                    else
                    {
                        header |= Text.Block(fileName).Bold();
                        header |= Text.Muted(fileDiff.FilePath);
                    }

                    if (onToggleFile != null)
                    {
                        var path = fileDiff.FilePath;
                        commitSheetContent |= new Box(header)
                            .BorderThickness(0).Padding(0)
                            .OnClick(() => onToggleFile(path));
                    }
                    else
                    {
                        commitSheetContent |= header;
                    }

                    if (isExpanded)
                    {
                        commitSheetContent |= new DiffView().Diff(fileDiff.Diff).Split().ShowHeader(false);
                    }
                }
            }
            else if (data.Files is { Count: > 0 })
            {
                var filesLayout = Layout.Vertical().Gap(1);
                filesLayout |= Text.Block("Changed Files").Bold();
                foreach (var (status, filePath) in data.Files)
                {
                    var (label, variant) = status switch
                    {
                        "A" => ("Added", BadgeVariant.Success),
                        "D" => ("Deleted", BadgeVariant.Destructive),
                        _ => ("Modified", BadgeVariant.Outline)
                    };
                    filesLayout |= Layout.Horizontal().Gap(2)
                        | new Badge(label).Variant(variant).Small()
                        | Text.Block(filePath);
                }
                commitSheetContent |= filesLayout;

                if (!string.IsNullOrWhiteSpace(data.Diff))
                {
                    commitSheetContent |= Text.Block("Diff").Bold();
                    commitSheetContent |= new DiffView().Diff(data.Diff).Split().ShowHeader(false);
                }
            }

            sheetContent = commitSheetContent;
        }

        return new Sheet(
            onClose: closeSheet,
            content: sheetContent,
            title: $"Commit {shortHash} — {data?.Title ?? ""}"
        ).Width(Size.Half()).Resizable();
    }

    internal static (Icons Icon, Colors Color) GetFileStatusIconAndColor(string status) => status switch
    {
        "A" => (Icons.FilePlus, Colors.Success),
        "D" => (Icons.FileMinus, Colors.Destructive),
        _ => (Icons.FilePen, Colors.Neutral)
    };

    public record AllChangesData(
        string? Diff,
        List<(string Status, string FilePath)> Files,
        int AddedCount,
        int ModifiedCount,
        int DeletedCount
    );

    public static AllChangesData? GetAllChangesData(PlanFile plan, IConfigService config, IGitService gitService)
    {
        if (plan.Commits.Count == 0) return null;

        var repoPaths = plan.GetEffectiveRepoPaths(config);
        var firstCommit = plan.Commits.First();
        var lastCommit = plan.Commits.Last();

        foreach (var repo in repoPaths)
        {
            var titleResult = gitService.GetCommitTitle(repo, firstCommit);
            if (!titleResult.IsSuccess) continue;

            string? diff;
            List<(string Status, string FilePath)>? files;

            if (plan.Commits.Count == 1)
            {
                var diffResult = gitService.GetCommitDiff(repo, firstCommit);
                var filesResult = gitService.GetCommitFiles(repo, firstCommit);
                diff = diffResult.IsSuccess ? diffResult.Value : null;
                files = filesResult.IsSuccess ? filesResult.Value : null;
            }
            else
            {
                var diffResult = gitService.GetCombinedDiff(repo, firstCommit, lastCommit);
                var filesResult = gitService.GetCombinedChangedFiles(repo, firstCommit, lastCommit);
                diff = diffResult.IsSuccess ? diffResult.Value : null;
                files = filesResult.IsSuccess ? filesResult.Value : null;
            }

            var fileList = files ?? [];
            var added = fileList.Count(f => f.Status == "A");
            var deleted = fileList.Count(f => f.Status == "D");
            var modified = fileList.Count - added - deleted;

            return new AllChangesData(diff, fileList, added, modified, deleted);
        }

        return null;
    }

    public static object RenderArtifactScreenshots(Dictionary<string, List<string>> artifacts)
    {
        if (!artifacts.TryGetValue("screenshots", out var screenshotFiles))
            return new Empty();

        var screenshotsLayout = Layout.Horizontal().Gap(2).Wrap();
        foreach (var file in screenshotFiles)
        {
            var imageUrl = $"/ivy/local-file?path={Uri.EscapeDataString(file)}";
            screenshotsLayout |= new Image(imageUrl)
            { ObjectFit = ImageFit.Contain, Alt = Path.GetFileName(file), Overlay = true }
                .Height(Size.Units(15)).Width(Size.Units(22))
                .BorderColor(Colors.Neutral)
                .BorderStyle(BorderStyle.Solid)
                .BorderThickness(1)
                .BorderRadius(BorderRadius.Rounded);
        }

        return screenshotsLayout;
    }
}
