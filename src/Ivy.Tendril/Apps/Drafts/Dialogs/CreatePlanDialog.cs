using Ivy.Core.Hooks;
using Ivy.Widgets.ContentInputView;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using System;
using System.IO;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class CreatePlanDialog(
    List<string> projectNames,
    Action<string, string[], int, string?> onCreatePlan,
    Action onClose,
    string[]? defaultProjects = null) : ViewBase
{
    private readonly string[] _defaultProjects = projectNames.Count == 1
        ? [projectNames[0]]
        : defaultProjects ?? ["Auto"];

    internal static readonly List<string> PriorityOptions = ["Normal", "High", "Urgent"];

    internal static int ParsePriority(string option) => option.ToLowerInvariant() switch
    {
        "normal" => 0,
        "high" => 1,
        "urgent" => 2,
        _ => 0
    };

    public override object Build()
    {
        var isCreating = UseState(false);
        var createPlanText = UseState("");
        var selectedProjects = UseState(_defaultProjects);
        var selectedPriority = UseState("Normal");
        var configService = UseService<IConfigService>();

        var uploadedFiles = UseState(new List<string>());

        var exclusiveProjects = new ConvertedState<string[], string[]>(
            selectedProjects,
            forward: v => v,
            backward: newValue =>
            {
                var current = selectedProjects.Value;
                if (newValue.Contains("Auto") && !current.Contains("Auto"))
                    return ["Auto"];
                if (newValue.Contains("Auto") && newValue.Any(p => p != "Auto"))
                    return newValue.Where(p => p != "Auto").ToArray();
                return newValue;
            }
        );

        var options = new List<IAnyOption>();
        if (projectNames.Count > 1)
            options.Add(new Option<string>("Auto", "Auto", icon: Icons.WandSparkles));
        options.AddRange(projectNames.Select(p => new Option<string>(p, p)));

        var planWasCreated = false;
        void HandleClose()
        {
            if (!planWasCreated)
            {
                foreach (var filePath in uploadedFiles.Value)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                    }
                    catch
                    {
                        // Best-effort cleanup
                    }
                }
            }
            onClose();
        }

        return new Dialog(
            _ => HandleClose(),
            new DialogHeader("Create New Plan"),
            new DialogBody(
                Layout.Vertical()
                | exclusiveProjects.ToSelectInput(options).Variant(SelectInputVariant.Toggle).WithField().Label("Select project(s)")
                | selectedPriority.ToSelectInput(PriorityOptions).Variant(SelectInputVariant.Toggle).WithField().Label("Priority")
                | new ContentInputView
                    {
                        OnUploadFile = async e =>
                        {
                            try
                            {
                                var name = e.Value?.Name;
                                var base64 = e.Value?.Base64Data;
                                Ivy.Helpers.CrashLog.Write($"[{DateTime.UtcNow:O}] OnUploadFile called. Name='{name ?? "null"}', Base64Length={base64?.Length ?? 0}");

                                var attachmentsDir = Path.Combine(configService.TendrilHome, "Attachments");
                                Directory.CreateDirectory(attachmentsDir);

                                var fileName = Path.GetFileName(e.Value.Name);
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                var ext = Path.GetExtension(fileName);
                                var uniqueName = $"{nameWithoutExt}_{Guid.NewGuid().ToString()[..8]}{ext}";
                                var filePath = Path.Combine(attachmentsDir, uniqueName);

                                var bytes = Convert.FromBase64String(e.Value.Base64Data);
                                await File.WriteAllBytesAsync(filePath, bytes);

                                var fileRef = $" [file: {filePath}]";
                                createPlanText.Set(createPlanText.Value + fileRef);

                                var newList = new List<string>(uploadedFiles.Value) { filePath };
                                uploadedFiles.Set(newList);
                            }
                            catch (Exception ex)
                            {
                                Ivy.Helpers.CrashLog.Write($"[{DateTime.UtcNow:O}] Error in OnUploadFile: {ex}");
                            }
                        },
                        OnSubmit = e =>
                        {
                            if (!string.IsNullOrWhiteSpace(createPlanText.Value) && !isCreating.Value)
                            {
                                isCreating.Set(true);
                                planWasCreated = true;
                                var projects = selectedProjects.Value.Any()
                                    ? selectedProjects.Value
                                    : projectNames.Count == 1 ? [projectNames[0]] : ["Auto"];
                                onCreatePlan(createPlanText.Value, projects, ParsePriority(selectedPriority.Value), null);
                                onClose();
                            }
                            return ValueTask.CompletedTask;
                        }
                    }
                    .Bind(createPlanText)
                    .SubmitLabel("Create")
                    .Placeholder("Enter task description...")
                    .WithField()
                    .Label("Describe the task for the new plan")
                    .Required()
            )
        ).Width(Size.Rem(30));
    }
}
