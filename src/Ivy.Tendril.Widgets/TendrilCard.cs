namespace Ivy.Tendril.Widgets;

/// <summary>
/// A single entry in the <see cref="TendrilCard"/> actions dropdown.
/// <paramref name="Tag"/> identifies the selected action in the OnMenuSelect event payload;
/// <paramref name="Icon"/> is an optional Lucide icon name (e.g. "Scissors").
/// </summary>
public record TendrilCardMenuItem(string Tag, string Label, string? Icon = null, bool Destructive = false);

/// <summary>
/// A footer metadata item: a small Lucide icon followed by a short label
/// (e.g. FileText + "01515", Timer + "5m 20s", Coins + "14K"). When
/// <paramref name="Tag"/> is set the item renders as a clickable button that
/// fires OnMetaClick with the tag.
/// </summary>
public record TendrilCardMeta(string Icon, string Label, string? Tag = null);

/// <summary>
/// A task card matching the Tendril Kanban board design: a status icon tile and a
/// tinted project pill in the top row (with an optional actions dropdown on the right),
/// a bold title, a muted status line with a leading icon, and a footer row of
/// icon + label metadata items. Intended to be used as the content of an Ivy
/// <c>KanbanCard</c>.
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

    /// <summary>
    /// Lucide icon rendered inside the top-left status tile (e.g. "Loader" for a
    /// running job, "Eye" for awaiting approval). Null hides the tile.
    /// </summary>
    [Prop] public string? Icon { get; init; }

    /// <summary>Spins <see cref="Icon"/> continuously (for in-progress states).</summary>
    [Prop] public bool IconSpin { get; init; }

    /// <summary>Label shown inside the tinted project pill (e.g. "Ivy-Tendril"). Null hides the pill.</summary>
    [Prop] public string? Project { get; init; }

    /// <summary>
    /// Base CSS color for the project pill (e.g. "#6366f1"). The pill background is a
    /// light tint of this color and the label uses the color itself. When null a color
    /// is derived deterministically from the project name.
    /// </summary>
    [Prop] public string? ProjectColor { get; init; }

    /// <summary>The muted status line shown under the title (e.g. "Awaiting approval").</summary>
    [Prop] public string? Status { get; init; }

    /// <summary>
    /// Lucide icon leading the status line (e.g. "Eye", "CornerDownRight").
    /// Null renders the status text without an icon.
    /// </summary>
    [Prop] public string? StatusIcon { get; init; }

    /// <summary>Footer metadata items spread across the bottom of the card.</summary>
    [Prop] public TendrilCardMeta[]? Meta { get; init; }

    /// <summary>
    /// UTC start time of the card's running job. When set the card renders a live
    /// elapsed-time meta item that ticks every second on the client, ahead of the
    /// trailing <see cref="Meta"/> items. Use this instead of a preformatted timer
    /// meta item for running jobs.
    /// </summary>
    [Prop] public DateTime? TimerStartedAt { get; init; }

    /// <summary>
    /// Actions shown in a dropdown opened from the "…" button in the card's top-right
    /// corner. The button only renders when this is set together with <see cref="OnMenuSelect"/>.
    /// </summary>
    [Prop] public TendrilCardMenuItem[]? MenuItems { get; init; }

    /// <summary>Fired when the card body is clicked. Payload is the card title.</summary>
    [Event] public Func<Event<TendrilCard, string>, ValueTask>? OnClick { get; init; }

    /// <summary>Fired when a dropdown action is selected. Payload is the item's Tag.</summary>
    [Event] public Func<Event<TendrilCard, string>, ValueTask>? OnMenuSelect { get; init; }

    /// <summary>Fired when a clickable meta item is clicked. Payload is the meta item's Tag.</summary>
    [Event] public Func<Event<TendrilCard, string>, ValueTask>? OnMetaClick { get; init; }
}

public static class TendrilCardExtensions
{
    // NOTE: All fluent setters use a "With" prefix rather than the bare prop name.
    // The Ivy framework ships same-named extension methods (e.g. MenuItem.Icon,
    // Card.Title) in the global Ivy namespace; an unprefixed setter here would
    // collide with those during overload resolution and fail with CS1929/CS1501.
    public static TendrilCard WithTitle(this TendrilCard w, string title) =>
        w with { Title = title };

    public static TendrilCard WithIcon(this TendrilCard w, string? icon, bool spin = false) =>
        w with { Icon = icon, IconSpin = spin };

    public static TendrilCard WithProject(this TendrilCard w, string? project) =>
        w with { Project = project };

    public static TendrilCard WithProject(this TendrilCard w, string project, string color) =>
        w with { Project = project, ProjectColor = color };

    public static TendrilCard WithStatus(this TendrilCard w, string? status, string? statusIcon = null) =>
        w with { Status = status, StatusIcon = statusIcon };

    public static TendrilCard WithMeta(this TendrilCard w, params TendrilCardMeta[] meta) =>
        w with { Meta = meta };

    public static TendrilCard WithTimerStartedAt(this TendrilCard w, DateTime? startedAt) =>
        w with { TimerStartedAt = startedAt };

    public static TendrilCard WithMenu(this TendrilCard w, params TendrilCardMenuItem[] items) =>
        w with { MenuItems = items };

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

    public static TendrilCard WithOnMenuSelect(
        this TendrilCard w,
        Func<Event<TendrilCard, string>, ValueTask> handler
    ) => w with { OnMenuSelect = handler };

    public static TendrilCard WithOnMenuSelect(this TendrilCard w, Action<string> handler) =>
        w with
        {
            OnMenuSelect = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };

    public static TendrilCard WithOnMetaClick(
        this TendrilCard w,
        Func<Event<TendrilCard, string>, ValueTask> handler
    ) => w with { OnMetaClick = handler };

    public static TendrilCard WithOnMetaClick(this TendrilCard w, Action<string> handler) =>
        w with
        {
            OnMetaClick = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            },
        };
}
