using Ivy.Tendril.Apps.Plans.Dialogs;

namespace Ivy.Tendril.Apps.PullRequest.Dialogs;

public class FollowUpDialog(
    string planId,
    string project,
    Action<string, string[], int> onCreatePlan,
    Action onClose) : ViewBase
{
    public override object Build()
    {
        var followUpText = UseState("");
        var selectedPriority = UseState("Normal");

        return new Dialog(
            _ => onClose(),
            new DialogHeader("Create Follow-Up Plan"),
            new DialogBody(
                Layout.Vertical()
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
                        var descriptionWithReference = $"{followUpText.Value} [Follows up on plan [{planId}]]";
                        onCreatePlan(descriptionWithReference, [project], CreatePlanDialog.ParsePriority(selectedPriority.Value));
                        onClose();
                    }
                })
            )
        ).Width(Size.Rem(32));
    }
}
