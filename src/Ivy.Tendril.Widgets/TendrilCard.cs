namespace Ivy.Tendril.Widgets;

/// <summary>
/// A compact task card matching the Tendril Kanban board design: a type badge with
/// a leading icon, an assignee avatar (initials on a colored circle), a bold title,
/// and a single footer description line. Intended to be used as the content of an
/// Ivy <c>KanbanCard</c>.
/// </summary>
[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "TendrilCard",
    GlobalName = "IvyTendrilWidgets"
)]
public record TendrilCard : WidgetBase<TendrilCard>
{
    public TendrilCard(string title) : base()
    {
        Title = title;
    }

    internal TendrilCard() { }

    /// <summary>The card's primary, bold title.</summary>
    [Prop] public string Title { get; init; } = string.Empty;

    /// <summary>Label shown inside the type badge (e.g. "Engagement", "Audit", "SOP").</summary>
    [Prop] public string? Badge { get; init; }

    /// <summary>
    /// Name of the Lucide icon rendered inside the type badge (e.g. "ScanLine").
    /// Null hides the badge icon. The badge itself only renders when <see cref="Badge"/> is set.
    /// </summary>
    [Prop] public string? BadgeIcon { get; init; } = "ScanLine";

    /// <summary>Assignee initials shown in the avatar (e.g. "JP"). Null hides the avatar.</summary>
    [Prop] public string? Assignee { get; init; }

    /// <summary>
    /// CSS color used for the assignee avatar background (any valid CSS color, e.g. "#e11d8f").
    /// When null a color is derived deterministically from the initials.
    /// </summary>
    [Prop] public string? AssigneeColor { get; init; }

    /// <summary>The muted footer description line shown at the bottom of the card.</summary>
    [Prop] public string? Footer { get; init; }

    /// <summary>Fired when the card body is clicked. Payload is the card title.</summary>
    [Event] public Func<Event<TendrilCard, string>, ValueTask>? OnClick { get; init; }
}

public static class TendrilCardExtensions
{
    // NOTE: All fluent setters use a "With" prefix rather than the bare prop name.
    // The Ivy framework ships same-named extension methods (e.g. MenuItem.Badge,
    // Card.Footer/Title) in the global Ivy namespace; an unprefixed setter here would
    // collide with those during overload resolution and fail with CS1929/CS1501.
    public static TendrilCard WithTitle(this TendrilCard w, string title) =>
        w with { Title = title };

    public static TendrilCard WithBadge(this TendrilCard w, string? badge) =>
        w with { Badge = badge };

    public static TendrilCard WithBadgeIcon(this TendrilCard w, string? badgeIcon) =>
        w with { BadgeIcon = badgeIcon };

    public static TendrilCard WithBadge(this TendrilCard w, string badge, string badgeIcon) =>
        w with { Badge = badge, BadgeIcon = badgeIcon };

    public static TendrilCard WithAssignee(this TendrilCard w, string? assignee) =>
        w with { Assignee = assignee };

    public static TendrilCard WithAssignee(this TendrilCard w, string assignee, string color) =>
        w with { Assignee = assignee, AssigneeColor = color };

    public static TendrilCard WithAssigneeColor(this TendrilCard w, string? color) =>
        w with { AssigneeColor = color };

    public static TendrilCard WithFooter(this TendrilCard w, string? footer) =>
        w with { Footer = footer };

    // NOTE: These fluent methods are intentionally NOT named OnClick. The widget
    // exposes an invocable delegate property of the same name (OnClick). A method
    // whose name matches a delegate-typed property is shadowed by delegate-invocation
    // member access for single-parameter lambdas, so `.OnClick(e => ...)` would bind
    // to the property and fail with CS1660. The property name must stay OnClick because
    // the external-widget serializer emits the event name from the property name.
    public static TendrilCard WithOnClick(
        this TendrilCard w,
        Func<Event<TendrilCard, string>, ValueTask> handler
    ) => w with { OnClick = handler };

    public static TendrilCard WithOnClick(this TendrilCard w, Action<string> handler) =>
        w with
        {
            OnClick = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static TendrilCard WithOnClick(this TendrilCard w, Action handler) =>
        w with
        {
            OnClick = _ =>
            {
                handler();
                return ValueTask.CompletedTask;
            },
        };
}
