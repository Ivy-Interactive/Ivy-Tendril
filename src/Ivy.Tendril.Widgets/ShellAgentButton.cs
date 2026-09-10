namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellAgentButton",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellAgentButton : WidgetBase<ShellAgentButton>
{
    [Prop] public string Label { get; init; } = "Agent";

    /// <summary>Icon name from AgentBranding.IconFor (e.g. "ClaudeCode"); mapped to a brand SVG client-side.</summary>
    [Prop] public string? Icon { get; init; }

    /// <summary>Single letter combined with Cmd+Opt (macOS) or Ctrl+Alt (Windows/Linux), handled client-side.</summary>
    [Prop] public string ShortcutKey { get; init; } = "A";

    /// <summary>Highlights the row while an agent session is the visible pane.</summary>
    [Prop] public bool IsActive { get; init; }

    [Event] public EventHandler<Event<ShellAgentButton>>? OnOpen { get; init; }
    [Event] public EventHandler<Event<ShellAgentButton>>? OnNewChat { get; init; }
}

public static class ShellAgentButtonExtensions
{
    public static ShellAgentButton Label(this ShellAgentButton w, string label) =>
        w with { Label = label };

    public static ShellAgentButton Icon(this ShellAgentButton w, string? icon) =>
        w with { Icon = icon };

    public static ShellAgentButton IsActive(this ShellAgentButton w, bool isActive) =>
        w with { IsActive = isActive };

    public static ShellAgentButton OnOpen(this ShellAgentButton w, Action handler) =>
        w with { OnOpen = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static ShellAgentButton OnNewChat(this ShellAgentButton w, Action handler) =>
        w with { OnNewChat = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
