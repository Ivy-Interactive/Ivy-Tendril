using Ivy.Tendril.Helpers;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Agent;

[App(title: "Chat", icon: Icons.MessageSquare, group: ["Apps"], order: Constants.Agent, isVisible: true, allowDuplicateTabs: true)]
public class AgentApp : ViewBase
{
    public override object Build()
    {
        var args = UseArgs<AgentAppArgs>();
        return new ChatWidget(args?.Prompt, args?.SessionId)
            .WithLayout()
            .Full()
            .RemoveParentPadding();
    }
}
