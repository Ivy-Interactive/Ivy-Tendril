using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.AgentViewer;

[App(title: "Timeout Case", icon: Icons.Clock, group: ["AgentViewer"])]
class TimeoutApp : ViewBase
{
    public override object Build()
    {
        return new Ivy.Tendril.Widgets.AgentViewer()
            .JsonStream(SampleData.TimedOutSession)
            .AutoScroll(false)
            .ShowStatusLabel(false)
            .Height(Size.Full());
    }
}
