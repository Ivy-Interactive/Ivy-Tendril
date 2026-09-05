using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Blades;

public class EditSkillBladeView(
    int? existingIndex,
    IState<List<ProjectSkillRef>> skills) : ViewBase
{
    public override object? Build()
    {
        var bladeContext = UseContext<IBladeContext>();
        var editName = UseState("");
        var editDescription = UseState("");
        var editInstructions = UseState("");
        var editPath = UseState("");

        UseEffect(() =>
        {
            var list = skills.Value;
            if (existingIndex is >= 0 && existingIndex < list.Count)
            {
                var sk = list[existingIndex.Value];
                editName.Set(sk.Name);
                editDescription.Set(sk.Description);
                editInstructions.Set(sk.Instructions ?? "");
                editPath.Set(sk.Path ?? "");
            }
        }, EffectTrigger.OnMount());

        var isNew = existingIndex == null;

        return Layout.Vertical()
            | editName.ToTextInput("Skill name (e.g. code-review)...").WithField().Label("Name").Required()
            | editDescription.ToTextInput("Short description...").WithField().Label("Description")
            | MarkdownFieldBuilders.BuildSkillInstructionsField(editInstructions)
            | editPath.ToTextInput("Path to skill folder/file (e.g. %TENDRIL_HOME%/Skills/my-skill)...").WithField().Label("File/Folder Path")
            | Layout.Horizontal()
                | new Button("Cancel").Outline().OnClick(() => bladeContext.Pop(this))
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
                        Disabled = false
                    };

                    if (isNew)
                        list.Add(entry);
                    else
                        list[existingIndex!.Value] = entry;

                    skills.Set(list);
                    bladeContext.Pop(this);
                });
    }
}
