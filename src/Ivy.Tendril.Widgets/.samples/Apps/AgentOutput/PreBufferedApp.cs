using Ivy;
using Ivy.Widgets.AgentOutput;

namespace Ivy.Tendril.Widgets.Samples;

[App(title: "Pre-buffered", icon: Icons.FileText, group: ["AgentOutput"])]
class PreBufferedApp : ViewBase
{
    record Props(
        bool AutoScroll = false,
        bool ShowThinking = false,
        bool ShowSystemEvents = false,
        bool ShowStatusLabel = true,
        string? StatusLabelOverride = null);

    public override object Build()
    {
        var props = UseState(new Props());

        var view = new AgentOutput()
            .JsonStream(SampleData.SuccessfulSession)
            .AutoScroll(props.Value.AutoScroll)
            .ShowThinking(props.Value.ShowThinking)
            .ShowSystemEvents(props.Value.ShowSystemEvents)
            .ShowStatusLabel(props.Value.ShowStatusLabel)
            .Height(Size.Full());

        if (!string.IsNullOrWhiteSpace(props.Value.StatusLabelOverride))
            view = view.StatusLabel(props.Value.StatusLabelOverride);

        return new SidebarLayout(
            view,
            props.ToForm("Apply")
        ).Resizable();
    }
}
