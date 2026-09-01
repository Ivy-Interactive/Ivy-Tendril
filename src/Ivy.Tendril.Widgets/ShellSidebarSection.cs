namespace Ivy.Tendril.Widgets;

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "ShellSidebarSection",
    GlobalName = "IvyTendrilWidgets"
)]
public record ShellSidebarSection : WidgetBase<ShellSidebarSection>
{
    [Prop] public string Title { get; init; } = "";
    [Prop] public List<ShellSectionItemDto> Items { get; init; } = new();
    [Prop] public string? SelectedId { get; init; }
    [Prop] public bool Searchable { get; init; }
    [Prop] public string? EmptyText { get; init; }

    [Event] public EventHandler<Event<ShellSidebarSection, string>>? OnSelectItem { get; init; }
    [Event] public EventHandler<Event<ShellSidebarSection>>? OnSearch { get; init; }
}

public static class ShellSidebarSectionExtensions
{
    public static ShellSidebarSection Title(this ShellSidebarSection w, string title) =>
        w with { Title = title };

    public static ShellSidebarSection Items(this ShellSidebarSection w, List<ShellSectionItemDto> items) =>
        w with { Items = items };

    public static ShellSidebarSection SelectedId(this ShellSidebarSection w, string? selectedId) =>
        w with { SelectedId = selectedId };

    public static ShellSidebarSection Searchable(this ShellSidebarSection w, bool searchable = true) =>
        w with { Searchable = searchable };

    public static ShellSidebarSection EmptyText(this ShellSidebarSection w, string? emptyText) =>
        w with { EmptyText = emptyText };

    public static ShellSidebarSection OnSelectItem(this ShellSidebarSection w, Action<string> handler) =>
        w with { OnSelectItem = new(e => { handler(e.Value); return ValueTask.CompletedTask; }) };

    public static ShellSidebarSection OnSearch(this ShellSidebarSection w, Action handler) =>
        w with { OnSearch = new(_ => { handler(); return ValueTask.CompletedTask; }) };
}
