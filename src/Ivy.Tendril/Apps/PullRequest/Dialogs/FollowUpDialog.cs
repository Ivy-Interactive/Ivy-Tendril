using Ivy.Core.Hooks;
using Ivy.Tendril.Apps.Plans.Dialogs;

namespace Ivy.Tendril.Apps.PullRequest.Dialogs;

public class FollowUpDialog(
    string planId,
    string planTitle,
    string prUrl,
    List<string> projectNames,
    Action<string, string[], int> onCreatePlan,
    Action onClose) : ViewBase
{
    public override object Build()
    {
        var followUpText = UseState("");
        var selectedProjects = UseState<string[]>(["Auto"]);
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

        var options = new List<string> { "Auto" };
        options.AddRange(projectNames);

        return new Dialog(
            _ => onClose(),
            new DialogHeader("Create Follow-Up Plan"),
            new DialogBody(
                Layout.Vertical()
                | Text.P($"Creating a follow-up plan for:").Muted()
                | Text.P($"#{planId} {planTitle}").Bold()
                | exclusiveProjects.ToSelectInput(options)
                    .Variant(SelectInputVariant.Toggle)
                    .WithField()
                    .Label("Select project(s)")
                | selectedPriority.ToSelectInput(CreatePlanDialog.PriorityOptions)
                    .Variant(SelectInputVariant.Toggle)
                    .WithField()
                    .Label("Priority")
                | followUpText.ToTextareaInput("Describe the follow-up task...")
                    .Rows(6)
                    .AutoFocus()
                    .WithField()
                    .Label("Follow-up task description")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(onClose),
                new Button("Create Follow-Up").Primary().ShortcutKey("Ctrl+Enter").OnClick(() =>
                {
                    if (!string.IsNullOrWhiteSpace(followUpText.Value))
                    {
                        var projects = selectedProjects.Value.Any() ? selectedProjects.Value : ["Auto"];
                        var descriptionWithReference = $"{followUpText.Value} [Follows up on plan [{planId}]]";
                        onCreatePlan(descriptionWithReference, projects, CreatePlanDialog.ParsePriority(selectedPriority.Value));
                        onClose();
                    }
                })
            )
        ).Width(Size.Rem(32));
    }
}
