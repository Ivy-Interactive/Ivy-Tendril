namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TerminalSessionHeader",
    GlobalName = "IvyTendrilWidgets"
)]
public record TerminalSessionHeader : WidgetBase<TerminalSessionHeader>
{
    [Prop] public string SessionId { get; init; } = "";
    [Prop] public string Title { get; init; } = "New Chat";
    [Prop] public List<ChatJobDto> Jobs { get; init; } = new();
    [Prop] public bool Spawned { get; init; }

    [Event] public EventHandler<Event<TerminalSessionHeader, string[]>>? OnRenameSession { get; init; }
    [Event] public EventHandler<Event<TerminalSessionHeader, string>>? OnDeleteSession { get; init; }
    [Event] public EventHandler<Event<TerminalSessionHeader>>? OnCreateSession { get; init; }
    [Event] public EventHandler<Event<TerminalSessionHeader>>? OnReviewJobs { get; init; }
}

public static class TerminalSessionHeaderExtensions
{
    public static TerminalSessionHeader SessionId(this TerminalSessionHeader w, string sessionId) =>
        w with { SessionId = sessionId };

    public static TerminalSessionHeader Title(this TerminalSessionHeader w, string title) =>
        w with { Title = title };

    public static TerminalSessionHeader Jobs(this TerminalSessionHeader w, List<ChatJobDto> jobs) =>
        w with { Jobs = jobs };

    public static TerminalSessionHeader Spawned(this TerminalSessionHeader w, bool spawned) =>
        w with { Spawned = spawned };

    public static TerminalSessionHeader OnRenameSession(this TerminalSessionHeader w, Action<string, string> handler) =>
        w with
        {
            OnRenameSession = new(e =>
            {
                if (e.Value is { Length: >= 2 }) handler(e.Value[0], e.Value[1]);
                return ValueTask.CompletedTask;
            })
        };

    public static TerminalSessionHeader OnDeleteSession(this TerminalSessionHeader w, Action<string> handler) =>
        w with { OnDeleteSession = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static TerminalSessionHeader OnCreateSession(this TerminalSessionHeader w, Action handler) =>
        w with { OnCreateSession = new(_ => { handler(); return ValueTask.CompletedTask; }) };

    public static TerminalSessionHeader OnReviewJobs(this TerminalSessionHeader w, Action handler) =>
        w with { OnReviewJobs = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
