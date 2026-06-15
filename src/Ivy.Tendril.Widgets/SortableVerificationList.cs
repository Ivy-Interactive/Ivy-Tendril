namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "SortableVerificationList",
    GlobalName = "IvyTendrilWidgets"
)]
public record SortableVerificationList : WidgetBase<SortableVerificationList>
{
    /// <summary>JSON array of verification items with name, enabled, and required properties.</summary>
    [Prop] public string? ItemsJson { get; init; }

    /// <summary>Fired when items are reordered via drag-and-drop. Value is JSON array of new indices.</summary>
    [Event] public Func<Event<SortableVerificationList, string>, ValueTask>? OnReorder { get; init; }

    /// <summary>Fired when an item's enabled or required state changes. Value is JSON object of updated item.</summary>
    [Event] public Func<Event<SortableVerificationList, string>, ValueTask>? OnChange { get; init; }
}

public static class SortableVerificationListExtensions
{
    public static SortableVerificationList ItemsJson(this SortableVerificationList w, string? itemsJson) =>
        w with { ItemsJson = itemsJson };

    // NOTE: These fluent methods are intentionally NOT named OnReorder/OnChange.
    // The widget exposes invocable delegate properties of the same name (OnReorder,
    // OnChange). A method whose name matches a delegate-typed property is shadowed by
    // delegate-invocation member access for single-parameter lambdas (e.g. json => ...),
    // so `.OnReorder(json => ...)` would bind to the property and fail with CS1660.
    // The property names must stay OnReorder/OnChange because the external-widget
    // serializer emits the event name from the property name (see WidgetSerializer).
    public static SortableVerificationList WithOnReorder(
        this SortableVerificationList w,
        Func<Event<SortableVerificationList, string>, ValueTask> handler
    ) => w with { OnReorder = handler };

    public static SortableVerificationList WithOnReorder(
        this SortableVerificationList w,
        Action<string> handler
    ) => w with
    {
        OnReorder = e =>
        {
            handler(e.Value);
            return ValueTask.CompletedTask;
        },
    };

    public static SortableVerificationList WithOnChange(
        this SortableVerificationList w,
        Func<Event<SortableVerificationList, string>, ValueTask> handler
    ) => w with { OnChange = handler };

    public static SortableVerificationList WithOnChange(
        this SortableVerificationList w,
        Action<string> handler
    ) => w with
    {
        OnChange = e =>
        {
            handler(e.Value);
            return ValueTask.CompletedTask;
        },
    };
}
