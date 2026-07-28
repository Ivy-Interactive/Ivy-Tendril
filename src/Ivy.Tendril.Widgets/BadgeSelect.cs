using Ivy;
using Ivy.Core;
using Ivy.Core.ExternalWidgets;
using Ivy.Core.Hooks;

namespace Ivy.Tendril.Widgets;

public record BadgeSelectOption(string Value, string Label, string? Icon = null, bool Removable = true);

[ExternalWidget(
    "frontend/dist/ivy-tendril-widgets.js",
    StylePath = "frontend/dist/ivy-tendril-widgets.css",
    ExportName = "BadgeSelect",
    GlobalName = "IvyTendrilWidgets"
)]
public record BadgeSelect : WidgetBase<BadgeSelect>
{
    [Prop] public BadgeSelectOption[] Options { get; init; } = [];
    [Prop] public string[] Value { get; init; } = [];
    [Prop] public string? Placeholder { get; init; }
    [Prop] public string? Icon { get; init; }
    [Prop] public bool Multiple { get; init; } = true;
    [Prop] public string? Tooltip { get; init; }

    [Event] public Func<Event<BadgeSelect, string[]>, ValueTask>? OnChange { get; init; }
}

public static class BadgeSelectExtensions
{
    public static BadgeSelect Options(this BadgeSelect w, params BadgeSelectOption[] options) =>
        w with { Options = options };

    public static BadgeSelect Options(this BadgeSelect w, IEnumerable<BadgeSelectOption> options) =>
        w with { Options = options.ToArray() };

    public static BadgeSelect Value(this BadgeSelect w, params string[] value) =>
        w with { Value = value };

    public static BadgeSelect Placeholder(this BadgeSelect w, string? placeholder) =>
        w with { Placeholder = placeholder };

    public static BadgeSelect Icon(this BadgeSelect w, string? icon) =>
        w with { Icon = icon };

    public static BadgeSelect Multiple(this BadgeSelect w, bool multiple = true) =>
        w with { Multiple = multiple };

    public static BadgeSelect Tooltip(this BadgeSelect w, string? tooltip) =>
        w with { Tooltip = tooltip };

    public static BadgeSelect WithOnChange(
        this BadgeSelect w,
        Func<Event<BadgeSelect, string[]>, ValueTask> handler
    ) => w with { OnChange = handler };

    public static BadgeSelect WithOnChange(this BadgeSelect w, Action<string[]> handler) =>
        w with
        {
            OnChange = e =>
            {
                handler(e.Value);
                return ValueTask.CompletedTask;
            }
        };

    public static BadgeSelect Bind(this BadgeSelect w, IState<string[]> state) =>
        w with
        {
            Value = state.Value,
            OnChange = e =>
            {
                state.Set(e.Value);
                return ValueTask.CompletedTask;
            }
        };
}
