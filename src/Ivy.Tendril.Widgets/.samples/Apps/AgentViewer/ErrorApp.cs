using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.AgentViewer;

[App(title: "Error Case", icon: Icons.CircleX, group: ["AgentViewer"])]
class ErrorApp : ViewBase
{
    public override object Build()
    {
        return new Ivy.Tendril.Widgets.AgentViewer()
            .JsonStream(SampleData.FailedSession)
            .AutoScroll(false)
            .ShowStatusLabel(false)
            .Height(Size.Full());
    }
}
