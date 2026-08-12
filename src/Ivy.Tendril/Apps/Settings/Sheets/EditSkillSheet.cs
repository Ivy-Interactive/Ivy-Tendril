using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Sheets;

public class EditSkillSheet(
    IState<bool> isOpen,
    int? editingIndex,
    IState<List<ProjectSkillRef>> skills) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editDescription = UseState("");
        var editInstructions = UseState("");
        var editPath = UseState("");
        var editDisabled = UseState(false);

        UseEffect(() =>
        {
            var list = skills.Value;
            if (editingIndex is >= 0 && editingIndex < list.Count)
            {
                var sk = list[editingIndex.Value];
                editName.Set(sk.Name);
                editDescription.Set(sk.Description);
                editInstructions.Set(sk.Instructions ?? "");
                editPath.Set(sk.Path ?? "");
                editDisabled.Set(sk.Disabled);
            }
        }, EffectTrigger.OnMount());

        var isNew = editingIndex == null;

        var sheetContent = Layout.Vertical()
            | editName.ToTextInput("Skill name (e.g. code-review)...").WithField().Label("Name").Required()
            | editDescription.ToTextInput("Short description...").WithField().Label("Description (optional)")
            | editInstructions.ToTextareaInput("Instructions / markdown rules...").Rows(5).WithField().Label("Inline Instructions (optional)")
            | editPath.ToTextInput("Path to skill folder/file (e.g. %TENDRIL_HOME%/Skills/my-skill)...").WithField().Label("File/Folder Path (optional)")
            | editDisabled.ToSwitchInput().WithField().Label("Disabled")
            | (Layout.Horizontal().AlignContent(Align.Right)
               | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
               | new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
               {
                   if (string.IsNullOrWhiteSpace(editName.Value)) return;

                   var list = new List<ProjectSkillRef>(skills.Value);
                   var entry = new ProjectSkillRef
                   {
                       Name = editName.Value.Trim(),
                       Description = editDescription.Value.Trim(),
                       Instructions = string.IsNullOrWhiteSpace(editInstructions.Value) ? null : editInstructions.Value,
                       Path = string.IsNullOrWhiteSpace(editPath.Value) ? null : editPath.Value,
                       Disabled = editDisabled.Value
                   };

                   if (isNew)
                       list.Add(entry);
                   else
                       list[editingIndex!.Value] = entry;

                   skills.Set(list);
                   isOpen.Set(false);
               }));

        return new Sheet(
            onClose: () => isOpen.Set(false),
            content: sheetContent,
            title: isNew ? "Add Custom Skill" : "Edit Custom Skill"
        ).Width(UxHelper.SheetWidth);
    }
}
