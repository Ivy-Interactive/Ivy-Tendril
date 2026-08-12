using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class EditReviewActionBladeView(
    int? existingIndex,
    IState<List<ReviewActionConfig>> reviewActions) : ViewBase
{
    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
        var editName = UseState("");
        var editCondition = UseState("");
        var editCommand = UseState("");

        UseEffect(() =>
        {
            var actions = reviewActions.Value;
            if (existingIndex is >= 0 && existingIndex < actions.Count)
            {
                editName.Set(actions[existingIndex.Value].Name);
                editCondition.Set(actions[existingIndex.Value].Condition);
                editCommand.Set(actions[existingIndex.Value].Command);
            }
        }, EffectTrigger.OnMount());

        var isNew = existingIndex == null;

        return Layout.Vertical()
            | editName.ToTextInput("Action name...").WithField().Label("Name").Required()
            | editCommand.ToTextareaInput("e.g. dotnet test").Rows(2).WithField().Label("Command").Required()
            | editCondition.ToTextareaInput("e.g. ${hasChanges}").Rows(2).WithField().Label("Condition (optional)")
            | Layout.Horizontal()
                | new Button("Cancel").Outline().OnClick(() => bladeContext.Pop(this))
                | new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    if (string.IsNullOrWhiteSpace(editName.Value)) return;
                    if (string.IsNullOrWhiteSpace(editCommand.Value)) return;

                    var list = new List<ReviewActionConfig>(reviewActions.Value);
                    if (isNew)
                        list.Add(new ReviewActionConfig { Name = editName.Value.Trim(), Condition = editCondition.Value, Command = editCommand.Value });
                    else
                        list[existingIndex!.Value] = new ReviewActionConfig { Name = editName.Value.Trim(), Condition = editCondition.Value, Command = editCommand.Value };

                    reviewActions.Set(list);
                    bladeContext.Pop(this);
                });
    }
}
