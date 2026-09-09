using Ivy.Core;

namespace Ivy.Tendril.Widgets;

/// <summary>
///     One thing the workspace's top bar or overflow menu can do. The widget only renders it and
///     reports the <paramref name="Tag" /> back through <c>OnAction</c>; the host decides what it means.
/// </summary>
/// <param name="Shortcut">A key the widget binds while nothing editable has focus, e.g. <c>E</c>, <c>Backspace</c>, <c>Ctrl+Enter</c>.</param>
/// <param name="Active">Renders the icon button as toggled on.</param>
/// <param name="FocusChat">Also puts the caret in the chat composer, so "Update" starts a conversation.</param>
public record PlanActionDto(
    string Tag,
    string Label,
    string? Icon = null,
    string? Shortcut = null,
    bool Active = false,
    bool Disabled = false,
    string? Badge = null,
    bool Loading = false,
    bool Danger = false,
    bool FocusChat = false);

public record PlanTabDto(string Id, string Label, string? Badge = null);

/// <summary>
///     The plan page frame shared by the Drafts and Review apps: a title bar with icon actions and a
///     primary button, a tab strip whose trailing corner holds the Verifications and Questions
///     dropdowns, the selected tab's content, and the plan's chat panel on the right, always
///     present and resizable by dragging its edge.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "PlanWorkspace",
    GlobalName = "IvyTendrilWidgets"
)]
[Slot("Content")]
[Slot("Chat")]
[Slot("Verifications")]
[Slot("Questions")]
[Slot("Toolbar")]
public record PlanWorkspace : WidgetBase<PlanWorkspace>
{
    public PlanWorkspace(
        object? content,
        object? chat = null,
        object? verifications = null,
        object? questions = null,
        object? toolbar = null)
        : base(BuildSlots(content, chat, verifications, questions, toolbar))
    {
    }

    private static object[] BuildSlots(object? content, object? chat, object? verifications, object? questions, object? toolbar) =>
    [
        content != null ? new Slot("Content", content) : new Slot("Content"),
        chat != null ? new Slot("Chat", chat) : new Slot("Chat"),
        verifications != null ? new Slot("Verifications", verifications) : new Slot("Verifications"),
        questions != null ? new Slot("Questions", questions) : new Slot("Questions"),
        toolbar != null ? new Slot("Toolbar", toolbar) : new Slot("Toolbar")
    ];

    [Prop] public string PlanId { get; init; } = string.Empty;
    [Prop] public string Title { get; init; } = string.Empty;
    [Prop] public string? Meta { get; init; }
    [Prop] public string? SourceUrl { get; init; }
    [Prop] public string? SourceLabel { get; init; }
    [Prop] public string? Persona { get; init; }
    [Prop] public string? PersonaInitials { get; init; }
    [Prop] public List<PlanActionDto> Actions { get; init; } = [];
    [Prop] public List<PlanActionDto> MenuItems { get; init; } = [];
    [Prop] public PlanActionDto? Primary { get; init; }
    [Prop] public List<PlanActionDto> Secondary { get; init; } = [];
    [Prop] public List<PlanTabDto> Tabs { get; init; } = [];
    [Prop] public string? SelectedTab { get; init; }
    [Prop] public int ChatWidth { get; init; } = 420;
    [Prop] public string VerificationsLabel { get; init; } = "Verifications";
    [Prop] public string QuestionsLabel { get; init; } = "Questions";

    /// <summary>Unanswered questions; a dot marks the Questions icon until its dropdown is opened for this plan.</summary>
    [Prop] public int UnansweredQuestions { get; init; }

    [Event] public EventHandler<Event<PlanWorkspace, string>>? OnTabSelect { get; init; }
    [Event] public EventHandler<Event<PlanWorkspace, string>>? OnAction { get; init; }
}

public static class PlanWorkspaceExtensions
{
    public static PlanWorkspace PlanId(this PlanWorkspace w, string planId) => w with { PlanId = planId };
    public static PlanWorkspace Title(this PlanWorkspace w, string title) => w with { Title = title };
    public static PlanWorkspace Meta(this PlanWorkspace w, string? meta) => w with { Meta = meta };

    public static PlanWorkspace Source(this PlanWorkspace w, string? url, string? label) =>
        w with { SourceUrl = url, SourceLabel = label };

    public static PlanWorkspace Persona(this PlanWorkspace w, string? persona, string? initials) =>
        w with { Persona = persona, PersonaInitials = initials };

    public static PlanWorkspace Actions(this PlanWorkspace w, IEnumerable<PlanActionDto> actions) =>
        w with { Actions = actions.ToList() };

    public static PlanWorkspace MenuItems(this PlanWorkspace w, IEnumerable<PlanActionDto> items) =>
        w with { MenuItems = items.ToList() };

    public static PlanWorkspace Primary(this PlanWorkspace w, PlanActionDto? primary) => w with { Primary = primary };

    public static PlanWorkspace Secondary(this PlanWorkspace w, IEnumerable<PlanActionDto> secondary) =>
        w with { Secondary = secondary.ToList() };

    public static PlanWorkspace Tabs(this PlanWorkspace w, IEnumerable<PlanTabDto> tabs) => w with { Tabs = tabs.ToList() };
    public static PlanWorkspace SelectedTab(this PlanWorkspace w, string? tabId) => w with { SelectedTab = tabId };
    public static PlanWorkspace ChatWidth(this PlanWorkspace w, int width) => w with { ChatWidth = width };

    public static PlanWorkspace VerificationsLabel(this PlanWorkspace w, string label) =>
        w with { VerificationsLabel = label };

    public static PlanWorkspace QuestionsLabel(this PlanWorkspace w, string label) => w with { QuestionsLabel = label };

    public static PlanWorkspace UnansweredQuestions(this PlanWorkspace w, int count) => w with { UnansweredQuestions = count };

    public static PlanWorkspace OnTabSelect(this PlanWorkspace w, Action<string> handler) =>
        w with { OnTabSelect = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static PlanWorkspace OnAction(this PlanWorkspace w, Action<string> handler) =>
        w with { OnAction = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };
}
