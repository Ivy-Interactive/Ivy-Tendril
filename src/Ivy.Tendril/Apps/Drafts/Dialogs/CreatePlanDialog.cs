using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Services;
using Ivy.Tendril.Helpers;
using System;
using System.IO;
using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Apps.Settings.Dialogs;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class CreatePlanDialog(
    List<string> projectNames,
    Action<string, string, int, string?> onCreatePlan,
    Action onClose,
    string? defaultProject = null) : ViewBase
{
    private readonly string _defaultProject = projectNames.Count == 1
        ? projectNames[0]
        : defaultProject == "Auto" || (defaultProject != null && projectNames.Contains(defaultProject))
            ? defaultProject!
            : "Auto";

    internal static readonly List<string> PriorityOptions = ["Normal", "High", "Urgent"];

    internal const string AddProjectActionValue = "__tendril_add_project__";

    internal static (BadgeSelectOption[] Options, BadgeSelectOption[] Actions) BuildProjectPickerOptions(
        IReadOnlyList<string> projectNames)
    {
        var options = new List<BadgeSelectOption>();
        if (projectNames.Count > 1)
            options.Add(new BadgeSelectOption("Auto", "Auto", "WandSparkles", Removable: false));
        options.AddRange(projectNames.Select(p => new BadgeSelectOption(p, p)));

        var actions = new[] { new BadgeSelectOption(AddProjectActionValue, "Add Project", "Plus") };
        return (options.ToArray(), actions);
    }

    internal static int ParsePriority(string option) => option.ToLowerInvariant() switch
    {
        "normal" => 0,
        "high" => 1,
        "urgent" => 2,
        _ => 0
    };

    // Builds the seed prompt for the "Continue with <agent>" flow. The description is
    // trimmed; a blank or "Auto" project lets the agent pick the project itself.
    internal static string BuildAgentPrompt(string project, string description)
    {
        var trimmed = description.Trim();

        if (string.IsNullOrEmpty(project) || project == "Auto")
            return $"I want to discuss creating a Tendril plan from this description: \"{trimmed}\". Determine the most appropriate project for it yourself.";

        return $"I want to discuss creating a Tendril plan for the project {project} from this description: \"{trimmed}\"";
    }

    public override object Build()
    {
        var nav = UseNavigation();
        var isCreating = UseState(false);
        var createPlanText = UseState("");
        var selectedProject = UseState(_defaultProject);
        var selectedPriority = UseState("Normal");
        var configService = UseService<IConfigService>();
        var agentRunner = UseService<IAgentRunner>();
        var client = UseService<IClientProvider>();
        var refreshToken = UseRefreshToken();
        var isAddProjectOpen = UseState(false);
        var uploadSessionId = UseState(() => Guid.NewGuid().ToString("N"));
        var (breakpoint, breakpointListener) = Context.UseBreakpoint();
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
            createPlanText.Set(createPlanText.Value + fileRef);

            var newList = new List<string>(uploadedFiles.Value) { filePath };
            uploadedFiles.Set(newList);
        });

        // e.g. "Continue with Claude Code" — branded to the configured coding agent.
        var continueLabel = $"Chat with {AgentBranding.For(configService.Settings.CodingAgent, agentRunner).Label}";

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

        var currentProjectNames = configService.Projects.Select(p => p.Name).ToList();

        UseEffect(() =>
        {
            if (selectedProject.Value == AddProjectActionValue)
            {
                selectedProject.Set("Auto");
                HandleClose();
                nav.Navigate<SettingsApp>(new SettingsAppArgs(SettingsApp.TagProjects));
            }
        }, selectedProject);

        object projectPickerWidget;

        if (currentProjectNames.Count <= 6)
        {
            var activeProject = string.IsNullOrEmpty(selectedProject.Value) ? "Auto" : selectedProject.Value;

            var cardsLayout = Layout.Grid().Columns(3).Gap(1);

            // First card is always Auto
            cardsLayout |= new Button("Auto")
                .Variant(activeProject == "Auto" ? ButtonVariant.Secondary : ButtonVariant.Ghost)
                .Width(Size.Full())
                .OnClick(() => selectedProject.Set("Auto"));

            // Project cards
            foreach (var proj in currentProjectNames)
            {
                var pName = proj;
                cardsLayout |= new Button(pName)
                    .Variant(activeProject == pName ? ButtonVariant.Secondary : ButtonVariant.Ghost)
                    .Width(Size.Full())
                    .OnClick(() => selectedProject.Set(pName));
            }

            // Option to add a new project
            cardsLayout |= new Button("Add Project")
                .Variant(ButtonVariant.Ghost)
                .Icon(Icons.Plus)
                .Width(Size.Full())
                .OnClick(() =>
                {
                    HandleClose();
                    nav.Navigate<SettingsApp>(new SettingsAppArgs(SettingsApp.TagProjects));
                });

            projectPickerWidget = cardsLayout;
        }
        else
        {
            var projectOptions = new List<Option<string>>
            {
                new Option<string>("Auto", "Auto")
            };
            projectOptions.AddRange(currentProjectNames.Select(p => new Option<string>(p, p)));
            projectOptions.Add(new Option<string>("+ Add New Project", AddProjectActionValue));

            projectPickerWidget = selectedProject.ToSelectInput(projectOptions)
                .Searchable(true)
                .Placeholder("Select project...")
                .Variant(SelectInputVariant.Select);
        }

        object contentInputWidget = new Ivy.Tendril.Widgets.ContentInput
        {
            UploadUrl = uploadContext.Value.UploadUrl,
            AutoFocus = true,
            OnSubmit = _ =>
            {
                if (!string.IsNullOrWhiteSpace(createPlanText.Value) && !isCreating.Value)
                {
                    isCreating.Set(true);
                    planWasCreated = true;
                    onCreatePlan(createPlanText.Value, selectedProject.Value, 0, uploadSessionId.Value);
                    onClose();
                }
                return ValueTask.CompletedTask;
            },
            OnMenuAction = e =>
            {
                if (e.Value == continueLabel)
                {
                    if (string.IsNullOrWhiteSpace(createPlanText.Value)) return ValueTask.CompletedTask;
                    planWasCreated = true;
                    var prompt = BuildAgentPrompt(selectedProject.Value, createPlanText.Value);
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
            .MenuOptions(continueLabel)
            .Placeholder("Enter task description...");

        var bodyContent = Layout.Vertical().Gap(2)
            | projectPickerWidget
            | contentInputWidget;

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

        return new Fragment(
            breakpointListener,
            planSurface);
    }
}
