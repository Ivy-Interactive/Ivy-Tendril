using Ivy;
using Ivy.Widgets.AgentOutput;

namespace WidgetSamples;

[App(title: "Live Stream", icon: Icons.Radio, group: ["AgentOutput"])]
class LiveStreamApp : ViewBase
{
    public override object Build()
    {
        var stream = UseStream<string>();
        var running = UseState(false);

        var button = running.Value
            ? new Button("Running...").Disabled()
            : new Button("Start").Primary().OnClick(async () =>
            {
                running.Set(true);
                foreach (var evt in SampleData.Events)
                {
                    stream.Write(evt);
                    await Task.Delay(600);
                }
                running.Set(false);
            });

        return Layout.Vertical().Height(Size.Full()).Gap(2)
               | button
               | new AgentOutput()
                   .Stream(stream)
                   .ShowStatusLabel(true)
                   .Height(Size.Full());
    }
}
