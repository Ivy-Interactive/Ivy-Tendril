using Ivy;
using Ivy.Core.Hooks;
using Ivy.Widgets.ContentInputView;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using System;
using System.IO;
using Ivy.Tendril.Apps.Agent;

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

    // Builds the seed prompt for the "Continue with Agent" flow. The description is
    // trimmed, a single project reads "the project X", multiple read "the projects X or Y",
    // and "Auto" lets the agent pick the project itself.
    internal static string BuildAgentPrompt(string[] projects, string description)
    {
        var trimmed = description.Trim();
        var realProjects = projects.Where(p => p != "Auto").ToArray();

        if (realProjects.Length == 0)
            return $"I want to discuss creating a Tendril plan from this description: \"{trimmed}\". Determine the most appropriate project for it yourself.";

        var projectWord = realProjects.Length == 1 ? "project" : "projects";
        var projectList = string.Join(" or ", realProjects);
        return $"I want to discuss creating a Tendril plan for the {projectWord} {projectList} from this description: \"{trimmed}\"";
    }

    public override object Build()
    {
        var nav = UseNavigation();
        var isCreating = UseState(false);
        var createPlanText = UseState("");
        var selectedProjects = UseState(_defaultProjects);
        var selectedPriority = UseState("Normal");
        var configService = UseService<IConfigService>();
        var uploadSessionId = UseState(() => Guid.NewGuid().ToString("N"));

        var (breakpoint, breakpointListener) = Context.UseBreakpoint();

        var uploadedFiles = UseState(new List<string>());

        var uploadContext = this.UseUpload(async (fileUpload, stream, token) =>
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
            createPlanText.Set(createPlanText.Value + fileRef);

            var newList = new List<string>(uploadedFiles.Value) { filePath };
            uploadedFiles.Set(newList);
        });

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
            onClose();
        }

        var bodyContent =
                Layout.Vertical()
                | exclusiveProjects.ToSelectInput(options).Variant(SelectInputVariant.Toggle).WithField().Label("Select Project(s)")
                | selectedPriority.ToSelectInput(PriorityOptions).Variant(SelectInputVariant.Toggle).WithField().Label("Priority")
                | new ContentInputView
                {
                    UploadUrl = uploadContext.Value.UploadUrl,
                    OnSubmit = e =>
                    {
                        if (!string.IsNullOrWhiteSpace(createPlanText.Value) && !isCreating.Value)
                        {
                            isCreating.Set(true);
                            planWasCreated = true;
                            var projects = selectedProjects.Value.Any()
                                ? selectedProjects.Value
                                : projectNames.Count == 1 ? [projectNames[0]] : ["Auto"];
                            onCreatePlan(createPlanText.Value, projects, ParsePriority(selectedPriority.Value), uploadSessionId.Value);
                            onClose();
                        }
                        return ValueTask.CompletedTask;
                    },
                    OnMenuAction = e =>
                    {
                        if (e.Value == "Continue with Agent")
                        {
                            if (string.IsNullOrWhiteSpace(createPlanText.Value)) return ValueTask.CompletedTask;
                            var projects = selectedProjects.Value.Any()
                                ? selectedProjects.Value
                                : projectNames.Count == 1 ? [projectNames[0]] : ["Auto"];
                            planWasCreated = true;
                            var prompt = BuildAgentPrompt(projects, createPlanText.Value);
                            nav.Navigate<AgentApp>(new AgentAppArgs(prompt));
                            onClose();
                        }
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
                        var currentText = createPlanText.Value;
                        if (currentText.Contains(fileRef))
                        {
                            createPlanText.Set(currentText.Replace(fileRef, ""));
                        }
                        else if (currentText.Contains(fileRef.Trim()))
                        {
                            createPlanText.Set(currentText.Replace(fileRef.Trim(), ""));
                        }
                        return ValueTask.CompletedTask;
                    }
                }
                    .Bind(createPlanText)
                    .SubmitLabel("Create")
                    .MenuOptions("Continue with Agent")
                    .Placeholder("Enter task description...")
                    .WithField()
                    .Label("Describe the task for the new plan")
                    .Required();

        object planSurface = breakpoint.Value == Breakpoint.Mobile
            ? new Sheet(
                _ => HandleClose(),
                bodyContent,
                title: "Create New Plan")
                .Side(SheetSide.Bottom)
                .Height(Size.Fit())
            : new Dialog(
                _ => HandleClose(),
                new DialogHeader("Create New Plan"),
                new DialogBody(bodyContent))
                .Width(Size.Rem(30));

        return new Fragment(breakpointListener, planSurface);
    }
}
