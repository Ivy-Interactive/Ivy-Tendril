using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.AgentViewer;

[App(title: "Table Output", icon: Icons.Table, group: ["AgentViewer"])]
class TableOutputApp : ViewBase
{
    public override object Build()
    {
        return new Ivy.Tendril.Widgets.AgentViewer()
            .JsonStream(SampleData.TableSession)
            .AutoScroll(false)
            .ShowStatusLabel(true)
            .Height(Size.Full());
    }
}
