using Ivy;

namespace Ivy.Tendril.Plugin.SampleWidget;

public class SampleWidgetDemoView : ViewBase
{
    public override object Build()
    {
        return Layout.Vertical().Padding(4).Gap(4)
            | Text.H1("External Widget Demo")
            | Text.Muted("This page uses a CounterWidget contributed by a plugin via [ExternalWidget].")
            | Layout.Horizontal().Gap(4)
                | new CounterWidget { Label = "Apples", InitialValue = 0, Step = 1 }
                | new CounterWidget { Label = "Oranges", InitialValue = 10, Step = 5 }
                | new CounterWidget { Label = "Score", InitialValue = 100, Step = 10 };
    }
}
