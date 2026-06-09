using Ivy;
using Ivy.Widgets.AgentOutput;

namespace Ivy.Tendril.Widgets.Samples;

[App(title: "Error Case", icon: Icons.CircleX, group: ["AgentOutput"])]
class ErrorApp : ViewBase
{
    public override object Build()
    {
        return new AgentOutput()
            .JsonStream(SampleData.FailedSession)
            .AutoScroll(false)
            .ShowStatusLabel(false)
            .Height(Size.Full());
    }
}
