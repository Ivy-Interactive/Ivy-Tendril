using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Views;

/// <summary>
///     What a plan page can do, collected as the DTOs the <see cref="PlanWorkspace" /> widget renders
///     paired with the handlers that run when the widget reports a tag back. Tags are unique across
///     icon actions, menu items and the labeled buttons, so one event serves them all.
/// </summary>
public sealed class PlanWorkspaceActions
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    public List<PlanActionDto> Actions { get; } = [];
    public List<PlanActionDto> MenuItems { get; } = [];
    public PlanActionDto? Primary { get; private set; }
    public List<PlanActionDto> Secondary { get; } = [];

    public PlanWorkspaceActions Action(
        string tag, string label, Icons icon, Action handler,
        string? shortcut = null, bool active = false, bool disabled = false, string? badge = null, bool focusChat = false)
    {
        Actions.Add(new PlanActionDto(tag, label, icon.ToString(), shortcut, active, disabled, badge, FocusChat: focusChat));
        _handlers[tag] = handler;
        return this;
    }

    public PlanWorkspaceActions Menu(
        string tag, string label, Icons? icon, Action handler,
        string? shortcut = null, bool disabled = false, bool danger = false)
    {
        MenuItems.Add(new PlanActionDto(tag, label, icon?.ToString(), shortcut, Disabled: disabled, Danger: danger));
        _handlers[tag] = handler;
        return this;
    }

    public PlanWorkspaceActions SetPrimary(
        string tag, string label, Icons icon, Action handler,
        string? shortcut = null, bool disabled = false, bool loading = false)
    {
        Primary = new PlanActionDto(tag, label, icon.ToString(), shortcut, Disabled: disabled, Loading: loading);
        _handlers[tag] = handler;
        return this;
    }

    public PlanWorkspaceActions AddSecondary(
        string tag, string label, Icons? icon, Action handler,
        string? shortcut = null, bool disabled = false, string? badge = null)
    {
        Secondary.Add(new PlanActionDto(tag, label, icon?.ToString(), shortcut, Disabled: disabled, Badge: badge));
        _handlers[tag] = handler;
        return this;
    }

    public void Dispatch(string tag)
    {
        if (_handlers.TryGetValue(tag, out var handler))
            handler();
    }

    public PlanWorkspace ApplyTo(PlanWorkspace workspace) =>
        workspace.Actions(Actions).MenuItems(MenuItems).Primary(Primary).Secondary(Secondary).OnAction(Dispatch);
}
