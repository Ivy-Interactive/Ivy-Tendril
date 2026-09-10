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
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Chat;
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
    string? projectName = null,
    Action? onDiscussWithAgent = null) : ViewBase
{
    public int FileCount => changesData?.Files.Count ?? 0;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var planService = UseService<IPlanReaderService>();
        var gitService = UseService<IGitService>();
        var config = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var shareContext = UseService<Ivy.Tendril.Services.Share.IShareContext>();
        var draftDiffCommentService = UseService<Ivy.Tendril.Services.Plans.IPlanDiffCommentService>();
        var hideFormatting = UseState(true);
        var showTree = UseState(true);

        var (suggestChangesDialog, showSuggestChangesDialog) = UseTrigger((isOpen) =>
        {
            if (!isOpen.Value) return null;
            return new SuggestChangesDialog(
                isOpen,
                selectedPlan,
                jobService,
                refreshPlans,
                draftComments.Value,
                draftComments
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

        var allFileDiffs = changesData.FileDiffs ?? PlanContentHelpers.SplitDiffByFile(changesData);

        if (allFileDiffs.Count == 0 && changesData.Files.Count == 0)
            return Text.Muted("No file changes.");

        var fileDiffs = allFileDiffs;
        var hiddenCount = 0;
        if (hideFormatting.Value)
        {
            fileDiffs = allFileDiffs.Where(fd => !PlanContentHelpers.IsFormattingOnly(fd)).ToList();
            hiddenCount = allFileDiffs.Count - fileDiffs.Count;
        }

        var changedFiles = fileDiffs.Select(fd =>
        {
            var counts = PlanContentHelpers.CountDiffLines(fd.Diff);
            return new ChangedFileDto(fd.FilePath, fd.Diff, counts.Additions, counts.Deletions);
        }).ToList();

        var changesView = new PlanChangesView
        {
            Key = $"changes:{selectedPlan.Id}",
            Files = changedFiles,
            Comments = draftComments.Value,
            CurrentAuthor = shareContext.IsShareMode ? shareContext.Persona : null,
            ShowTree = showTree.Value,
            OnAddComment = async e =>
            {
                var comment = e.Value;
                if (string.IsNullOrEmpty(comment.Author) && shareContext.IsShareMode)
                {
                    comment = comment with { Author = shareContext.Persona };
                }
                var list = new List<DraftComment>(draftComments.Value) { comment };
                draftComments.Set(list);
                await draftDiffCommentService.SaveDraftCommentsAsync(selectedPlan.FolderPath, list);
            },
            OnUpdateComment = async e =>
            {
                var c = e.Value;
                var list = new List<DraftComment>(draftComments.Value);
                var idx = list.FindIndex(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                if (idx >= 0)
                {
                    list[idx] = c;
                    draftComments.Set(list);
                    await draftDiffCommentService.SaveDraftCommentsAsync(selectedPlan.FolderPath, list);
                }
            },
            OnDeleteComment = async e =>
            {
                var c = e.Value;
                var list = new List<DraftComment>(draftComments.Value);
                list.RemoveAll(dc => dc.FilePath == c.FilePath && dc.ChangeKey == c.ChangeKey);
                draftComments.Set(list);
                await draftDiffCommentService.SaveDraftCommentsAsync(selectedPlan.FolderPath, list);
            },
            OnDirectEdit = async e =>
            {
                await HandleDirectEdit(e.Value);
            }
        }.Width(Size.Full()).Height(Size.Full());

        var treeButton = new TendrilIconButton(showTree.Value ? "Hide file tree" : "Show file tree", "ListTree")
            .Size(TendrilIconButtonSize.Md)
            .Active(showTree.Value)
            .OnClick(() => showTree.Set(!showTree.Value));

        var formattingButton = new TendrilIconButton(
                hideFormatting.Value ? "Show formatting changes" : "Hide formatting changes", "EyeOff")
            .Size(TendrilIconButtonSize.Md)
            .Active(hideFormatting.Value)
            .OnClick(() => hideFormatting.Set(!hideFormatting.Value));

        var leftSide = Layout.Horizontal().Gap(1).AlignContent(Align.Left)
            | treeButton
            | formattingButton;

        if (hideFormatting.Value && hiddenCount > 0)
            leftSide |= Text.Muted($"{fileDiffs.Count} of {allFileDiffs.Count} files (hiding {hiddenCount} formatting-only)").Small();

        var totals = PlanContentHelpers.CountDiffLines(fileDiffs);
        var totalsText = Text.Rich().NoWrap().Small()
            .Run($"+{totals.Additions}", color: Colors.Success)
            .Run($" -{totals.Deletions}", color: Colors.Destructive);

        var rightSide = Layout.Horizontal().Gap(2).AlignContent(Align.Right)
            | totalsText;

        var toolbar = Layout.Horizontal().Width(Size.Full()).AlignContent(Align.SpaceBetween).Height(Size.Auto()).Padding(2, 0, 4, 0)
            | leftSide
            | rightSide;

        // Padding order is (left, top, right, bottom).
        var mainLayout = Layout.Horizontal().Height(Size.Full().Min(Size.Px(0))).Padding(2, 0, 4, 4)
            | changesView;

        var outer = Layout.Vertical().Height(Size.Full().Min(Size.Px(0)));
        if (mismatchBanner != null)
            outer |= mismatchBanner;
        outer |= toolbar;
        outer |= mainLayout;
        outer |= suggestChangesDialog;
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
}
