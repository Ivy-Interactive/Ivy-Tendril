using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Agent;

namespace Ivy.Tendril.Apps.Drafts.Dialogs;

public class CreatePlanDialog(
    List<string> projectNames,
    Action<string, string[], int> onCreatePlan,
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
        var nav = UseNavigation();
        var isCreating = UseState(false);
        var createPlanText = UseState("");
        var selectedProjects = UseState(_defaultProjects);
        var selectedPriority = UseState("Normal");

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

        return new Dialog(
            _ => onClose(),
            new DialogHeader("Create New Plan"),
            new DialogBody(
                Layout.Vertical()
                | exclusiveProjects.ToSelectInput(options).Variant(SelectInputVariant.Toggle).WithField().Label("Select Project(s)")
                | selectedPriority.ToSelectInput(PriorityOptions).Variant(SelectInputVariant.Toggle).WithField().Label("Priority")
                | createPlanText.ToTextareaInput("Enter task description...").Rows(6).AutoFocus().WithField()
                    .Label("Describe what you want to accomplish")
                    .Required()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(onClose),
                new Button("Continue with Agent").Outline().Icon(Icons.MessageCircle).Disabled(string.IsNullOrWhiteSpace(createPlanText.Value)).OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(createPlanText.Value)) return;
                    var projects = selectedProjects.Value.Any()
                        ? selectedProjects.Value
                        : projectNames.Count == 1 ? [projectNames[0]] : ["Auto"];
                    var projectNamesStr = string.Join(", ", projects);
                    var prompt = $"User wants to chat about creating a Tendril plan for project {projectNamesStr} with the description \"{createPlanText.Value}\"";
                    nav.Navigate<AgentApp>(new AgentAppArgs(prompt));
                    onClose();
                }),
                new Button("Create").Primary().Disabled(isCreating.Value || string.IsNullOrWhiteSpace(createPlanText.Value)).ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(createPlanText.Value) && !isCreating.Value)
                    {
                        isCreating.Set(true);
                        var projects = selectedProjects.Value.Any()
                            ? selectedProjects.Value
                            : projectNames.Count == 1 ? [projectNames[0]] : ["Auto"];
                        onCreatePlan(createPlanText.Value, projects, ParsePriority(selectedPriority.Value));
                        onClose();
                    }
                })
            )
        ).Width(Size.Rem(30));
    }
}
