using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

/// <summary>
///     Adds or edits one of a project's named service ports. The default port is only a preference: a
///     plan worktree falls back to a free port when it is taken, so two reviews can run at once (see
///     <see cref="Helpers.PortAllocationHelper" />).
/// </summary>
internal class EditProjectPortDialog(
    IState<bool> isOpen,
    string? existingName,
    IState<Dictionary<string, ProjectPortConfig>> ports) : ViewBase
{
    public override object? Build()
    {
        var editName = UseState("");
        var editPort = UseState(3000);
        var editDescription = UseState("");

        UseEffect(() =>
        {
            if (existingName != null && ports.Value.TryGetValue(existingName, out var existing))
            {
                editName.Set(existingName);
                editPort.Set(existing.DefaultPort);
                editDescription.Set(existing.Description);
            }
        }, EffectTrigger.OnMount());

        var isNew = existingName == null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader(isNew ? "Add Port" : "Edit Port"),
            new DialogBody(
                Layout.Vertical()
                | editName.ToTextInput("e.g. backend").WithField().Label("Name").Required()
                | editPort.ToNumberInput().Min(1).Max(65535).WithField().Label("Default Port").Required()
                | editDescription.ToTextInput("What listens here...").WithField().Label("Description")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button(isNew ? "Add" : "Save").Primary().OnClick(() =>
                {
                    var name = editName.Value.Trim();
                    if (string.IsNullOrWhiteSpace(name)) return;
                    if (editPort.Value is < 1 or > 65535) return;

                    var updated = new Dictionary<string, ProjectPortConfig>(ports.Value);

                    // A rename drops the old key, so an env file's ${ports.<old>} stops resolving rather
                    // than silently keeping a stale duplicate entry alive.
                    if (existingName != null && existingName != name)
                        updated.Remove(existingName);

                    updated[name] = new ProjectPortConfig
                    {
                        DefaultPort = editPort.Value,
                        Description = editDescription.Value
                    };

                    ports.Set(updated);
                    isOpen.Set(false);
                })
            )
        ).Width(Size.Rem(30));
    }
}
