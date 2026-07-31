namespace Ivy.Tendril.Plugin.SampleWidget;

/// <summary>
/// A simple counter widget with increment/decrement buttons, rendered entirely in React.
/// Demonstrates how plugins can contribute external widgets with frontend interactivity.
/// </summary>
[ExternalWidget("frontend/dist/Ivy_Tendril_Plugin_SampleWidget.js", ExportName = "Counter")]
public record CounterWidget : WidgetBase<CounterWidget>
{
    [Prop] public string Label { get; init; } = "Count";
    [Prop] public int InitialValue { get; init; } = 0;
    [Prop] public int Step { get; init; } = 1;
    [Prop] public Colors? Color { get; init; }
}

public static class CounterWidgetExtensions
{
    public static CounterWidget Label(this CounterWidget w, string label) =>
        w with { Label = label };

    public static CounterWidget InitialValue(this CounterWidget w, int value) =>
        w with { InitialValue = value };

    public static CounterWidget Step(this CounterWidget w, int step) =>
        w with { Step = step };

    public static CounterWidget Color(this CounterWidget w, Colors color) =>
        w with { Color = color };
}
