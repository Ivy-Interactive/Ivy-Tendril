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

    internal const int MaxProjectsForToggleVariant = 6;

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

    internal static List<Option<string>> BuildProjectSelectOptions(IReadOnlyList<string> projectNames)
    {
        var options = new List<Option<string>>();
        if (projectNames.Count > 1 || projectNames.Count == 0)
        {
            options.Add(new Option<string>("Auto", "Auto", icon: Icons.WandSparkles));
        }
        options.AddRange(projectNames.Select(p => new Option<string>(p, p)));
        options.Add(new Option<string>("+ Add New Project", AddProjectActionValue));
        return options;
    }

    internal static SelectInputVariant GetProjectPickerVariant(int projectCount) =>
        projectCount <= MaxProjectsForToggleVariant
            ? SelectInputVariant.Toggle
            : SelectInputVariant.Select;

    internal static bool IsProjectPickerSearchable(int projectCount) =>
        projectCount > MaxProjectsForToggleVariant;

    internal static int ParsePriority(string option) => option.ToLowerInvariant() switch
    {
        "normal" => 0,
        "high" => 1,
        "urgent" => 2,
        _ => 0
    };

    // Builds the seed prompt for the "Continue with <agent>" flow. The description is
    // trimmed; a blank or "Auto" project lets the agent pick the project itself.
    internal static string BuildAgentPrompt(
        string project,
        string description,
        ProjectConfig? projectConfig = null,
        IReadOnlyList<string>? attachedFiles = null,
        IReadOnlyList<ProjectConfig>? availableProjects = null)
    {
        var trimmed = description.Trim();
        var isAuto = string.IsNullOrEmpty(project) || project == "Auto";

        var sb = new System.Text.StringBuilder();

        if (isAuto)
            sb.AppendLine($"I want to discuss creating a Tendril plan from this description: \"{trimmed}\". Determine the most appropriate project for it yourself.");
        else
            sb.AppendLine($"I want to discuss creating a Tendril plan for the project {project} from this description: \"{trimmed}\"");

        var hasProjectConfig = projectConfig != null;
        var hasAttachedFiles = attachedFiles != null && attachedFiles.Count > 0;
        var hasAvailableProjects = isAuto && availableProjects != null && availableProjects.Count > 0;

        if (hasProjectConfig || hasAttachedFiles || hasAvailableProjects)
        {
            if (hasProjectConfig && projectConfig != null)
            {
                sb.AppendLine();
                sb.AppendLine("### Project Context");
                sb.AppendLine($"- **Project:** {projectConfig.Name}");
                if (projectConfig.Repos.Count > 0)
                {
                    sb.AppendLine("- **Repositories:**");
                    foreach (var repo in projectConfig.Repos)
                    {
                        var branchInfo = !string.IsNullOrEmpty(repo.BaseBranch) ? $" (branch: {repo.BaseBranch})" : "";
                        sb.AppendLine($"  - {repo.Path}{branchInfo}");
                    }
                }
                if (projectConfig.Verifications.Count > 0)
                {
                    var verifs = string.Join(", ", projectConfig.Verifications.Select(v => v.Name));
                    sb.AppendLine($"- **Configured Verifications:** {verifs}");
                }
                if (!string.IsNullOrWhiteSpace(projectConfig.Context))
                {
                    sb.AppendLine($"- **Notes:** {projectConfig.Context.Trim()}");
                }
            }
            else if (hasAvailableProjects && availableProjects != null)
            {
                sb.AppendLine();
                sb.AppendLine("### Available Projects");
                foreach (var p in availableProjects)
                {
                    if (p.Repos.Count > 0)
                    {
                        var repoList = string.Join(", ", p.Repos.Select(r => !string.IsNullOrEmpty(r.BaseBranch) ? $"{r.Path} (branch: {r.BaseBranch})" : r.Path));
                        sb.AppendLine($"- **{p.Name}**: {repoList}");
                    }
                    else
                    {
                        sb.AppendLine($"- **{p.Name}**");
                    }
                }
            }

            if (hasAttachedFiles && attachedFiles != null)
            {
                sb.AppendLine();
                sb.AppendLine("### Attached Files");
                foreach (var file in attachedFiles)
                {
                    sb.AppendLine($"- {file}");
                }
            }

            sb.AppendLine();
            var targetProject = !isAuto ? project : "<project-name>";
            sb.AppendLine("### Guidance");
            sb.AppendLine($"Please research the repository paths and inspect any attached files. Discuss the implementation approach, and when ready, initiate plan creation using: `tendril job start CreatePlan --description=\"...\" --project=\"{targetProject}\"`.");
        }

        return sb.ToString().TrimEnd();
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

        // e.g. "Continue with Claude Code" - branded to the configured coding agent.
        var continueLabel = $"Chat with {AgentBranding.For(configService.Settings.CodingAgent, agentRunner, configService).Label}";

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

        var options = BuildProjectSelectOptions(currentProjectNames);

        if (GetProjectPickerVariant(currentProjectNames.Count) == SelectInputVariant.Toggle)
        {
            projectPickerWidget = selectedProject.ToSelectInput(options)
                .Variant(SelectInputVariant.Toggle);
        }
        else
        {
            projectPickerWidget = selectedProject.ToSelectInput(options)
                .Searchable(IsProjectPickerSearchable(currentProjectNames.Count))
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
                    var proj = selectedProject.Value == "Auto" || string.IsNullOrEmpty(selectedProject.Value)
                        ? null
                        : configService.GetProject(selectedProject.Value);
                    var prompt = BuildAgentPrompt(
                        selectedProject.Value,
                        createPlanText.Value,
                        proj,
                        uploadedFiles.Value,
                        configService.Projects);
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
