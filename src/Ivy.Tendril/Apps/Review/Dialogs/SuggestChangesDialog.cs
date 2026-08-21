using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Review.Dialogs;

public class SuggestChangesDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IJobService jobService,
    Action refreshPlans,
    List<DraftComment>? draftComments = null,
    IState<List<DraftComment>>? draftCommentsState = null) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IJobService _jobService = jobService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;
    private readonly List<DraftComment>? _draftComments = draftComments;
    private readonly IState<List<DraftComment>>? _draftCommentsState = draftCommentsState;

    public override object? Build()
    {
        var configService = UseService<IConfigService>();
        var draftDiffCommentService = UseService<Ivy.Tendril.Services.Plans.IDraftDiffCommentService>();
        var isCreating = UseState(false);
        var suggestText = UseState("");
        var uploadSessionId = UseState(() => Guid.NewGuid().ToString("N"));
        var uploadedFiles = UseState(new List<string>());

        var uploadContext = UseUpload(async (fileUpload, stream, token) =>
        {
            var tempDir = Path.Combine(configService.TendrilHome, "Attachments", uploadSessionId.Value);
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(fileUpload.FileName);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName).Replace(" ", "_");
            var ext = Path.GetExtension(fileName);
            var uniqueName = $"{nameWithoutExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
            var filePath = Path.Combine(tempDir, uniqueName);

            await using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream, token);
            }

            var fileRef = $" [file: {filePath}]";
            suggestText.Set(suggestText.Value + fileRef);

            var newList = new List<string>(uploadedFiles.Value) { filePath };
            uploadedFiles.Set(newList);
        });

        if (!_dialogOpen.Value) return null;

        var commentCount = _draftComments?.Count ?? 0;

        void HandleSubmit()
        {
            if (isCreating.Value) return;
            if (commentCount == 0 && string.IsNullOrWhiteSpace(suggestText.Value)) return;
            isCreating.Set(true);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(suggestText.Value))
            {
                sb.AppendLine(suggestText.Value.Trim());
                sb.AppendLine();
            }

            if (_draftComments != null && _draftComments.Count > 0)
            {
                var repos = _selectedPlan.GetEffectiveRepoPaths(configService);
                var repoPath = repos.FirstOrDefault() ?? "";

                sb.AppendLine("Line-by-line feedback:");
                foreach (var c in _draftComments)
                {
                    var absolutePath = Path.Combine(repoPath, c.FilePath).Replace('\\', '/');
                    var fileLink = $"file:///{absolutePath.TrimStart('/')}";
                    sb.AppendLine(!string.IsNullOrEmpty(c.Author)
                        ? $"- **In [{c.FilePath}]({fileLink}#L{c.LineNumber}) line {c.LineNumber}** (by {c.Author}):"
                        : $"- **In [{c.FilePath}]({fileLink}#L{c.LineNumber}) line {c.LineNumber}**:");
                    sb.AppendLine($"  {c.Content}");
                }
            }

            var feedback = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(feedback))
            {
                feedback = "Look at inline comments, implement changes, and come back with a new plan.";
            }

            _jobService.StartJob(new RetryPlanArgs(_selectedPlan.FolderPath, feedback));
            if (_draftCommentsState != null)
            {
                _draftCommentsState.Set(new List<DraftComment>());
            }
            _ = draftDiffCommentService.ClearDraftCommentsAsync(_selectedPlan.FolderPath);
            _refreshPlans();
            _dialogOpen.Set(false);
        }

        var submitLabel = commentCount > 0 ? $"Request Changes ({commentCount} inline)" : "Request Changes";

        return new Dialog(
            _ => _dialogOpen.Set(false),
            new DialogHeader($"Request Changes for Plan #{_selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | Text.P("Provide suggestions or instructions for changes to the implementation.")
                | (commentCount > 0
                    ? Callout.Info($"{commentCount} inline comment(s) on file diffs will be included with your feedback.")
                    : null)
                | new Ivy.Tendril.Widgets.ContentInput
                {
                    UploadUrl = uploadContext.Value.UploadUrl,
                    AutoFocus = true,
                    OnSubmit = _ =>
                    {
                        HandleSubmit();
                        return ValueTask.CompletedTask;
                    },
                    OnRemoveAttachment = e =>
                    {
                        var filePath = e.Value;
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                File.Delete(filePath);
                            }
                        }
                        catch
                        {
                            // ignore
                        }
                        var newList = new List<string>(uploadedFiles.Value);
                        newList.Remove(filePath);
                        uploadedFiles.Set(newList);

                        var fileRef = $" [file: {filePath}]";
                        var currentText = suggestText.Value;
                        if (currentText.Contains(fileRef))
                        {
                            suggestText.Set(currentText.Replace(fileRef, ""));
                        }
                        else if (currentText.Contains(fileRef.Trim()))
                        {
                            suggestText.Set(currentText.Replace(fileRef.Trim(), ""));
                        }
                        return ValueTask.CompletedTask;
                    }
                }
                    .Bind(suggestText)
                    .SubmitLabel(submitLabel)
                    .Placeholder("Enter your suggestions...")
            )
        ).Width(Size.Rem(30));
    }
}
