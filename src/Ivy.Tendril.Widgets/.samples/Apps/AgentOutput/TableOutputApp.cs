using Ivy;
using Ivy.Widgets.AgentOutput;

namespace WidgetSamples;

[App(title: "Table Output", icon: Icons.Table, group: ["AgentOutput"])]
class TableOutputApp : ViewBase
{
    public override object Build()
    {
        return new AgentOutput()
            .JsonStream(SampleData.TableSession)
            .AutoScroll(false)
            .ShowStatusLabel(true)
            .Height(Size.Full());
    }
}
