using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class UpdatePlanDialog(
    IState<bool> dialogOpen,
    PlanFile selectedPlan,
    IState<PlanFile?> selectedPlanState,
    IJobService jobService,
    Action refreshPlans) : ViewBase
{
    private readonly IState<bool> _dialogOpen = dialogOpen;
    private readonly IJobService _jobService = jobService;
    private readonly Action _refreshPlans = refreshPlans;
    private readonly PlanFile _selectedPlan = selectedPlan;
    private readonly IState<PlanFile?> _selectedPlanState = selectedPlanState;

    public override object? Build()
    {
        var configService = UseService<IConfigService>();
        var isCreating = UseState(false);
        var updateText = UseState("");
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
            updateText.Set(updateText.Value + fileRef);

            var newList = new List<string>(uploadedFiles.Value) { filePath };
            uploadedFiles.Set(newList);
        });

        if (!_dialogOpen.Value) return null;

        // Check if there's already an UpdatePlan job running for this plan
        var hasActiveJob = _jobService.GetJobs().Any(j =>
            j.TypedArgs is UpdatePlanArgs &&
            j.Status is JobStatus.Running or JobStatus.Queued or JobStatus.Pending &&
            j.TypedArgs?.PlanFolder != null &&
            j.TypedArgs.PlanFolder.Equals(_selectedPlan.FolderPath, StringComparison.OrdinalIgnoreCase));

        var jobWasStarted = false;
        void HandleClose()
        {
            if (!jobWasStarted)
            {
                var tempDir = Path.Combine(configService.TendrilHome, "Attachments", uploadSessionId.Value);
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
            _dialogOpen.Set(false);
        }

        return new Dialog(
            _ => HandleClose(),
            new DialogHeader($"Update Plan #{_selectedPlan.Id}"),
            new DialogBody(
                Layout.Vertical()
                | Text.P("Provide instructions for revising this draft plan.")
                | (hasActiveJob ? Text.P("⚠️ UpdatePlan is already running for this plan. Please wait...").Color(Colors.Warning) : null)
                | new Ivy.Tendril.Widgets.ContentInput
                {
                    UploadUrl = uploadContext.Value.UploadUrl,
                    AutoFocus = true,
                    OnSubmit = _ =>
                    {
                        if (hasActiveJob || isCreating.Value || string.IsNullOrWhiteSpace(updateText.Value))
                            return ValueTask.CompletedTask;

                        isCreating.Set(true);
                        jobWasStarted = true;

                        // Optimistically update UI state before disk I/O
                        var optimisticPlan = _selectedPlan with
                        {
                            Metadata = _selectedPlan.Metadata with { State = PlanStatus.Updating }
                        };
                        _selectedPlanState.Set(optimisticPlan);

                        // Plan transition (and pre-state snapshot) handled by JobService.StartJob.
                        _jobService.StartJob(new UpdatePlanArgs(_selectedPlan.FolderPath, updateText.Value, uploadSessionId.Value));
                        _refreshPlans();
                        _dialogOpen.Set(false);

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
                        var currentText = updateText.Value;
                        if (currentText.Contains(fileRef))
                        {
                            updateText.Set(currentText.Replace(fileRef, ""));
                        }
                        else if (currentText.Contains(fileRef.Trim()))
                        {
                            updateText.Set(currentText.Replace(fileRef.Trim(), ""));
                        }
                        return ValueTask.CompletedTask;
                    }
                }
                    .Bind(updateText)
                    .SubmitLabel("Update")
                    .Placeholder("Enter update instructions...")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => HandleClose())
            )
        ).Width(Size.Rem(30));
    }
}
