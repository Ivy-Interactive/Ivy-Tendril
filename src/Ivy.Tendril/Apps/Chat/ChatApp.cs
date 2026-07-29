using Ivy.Tendril.Apps.Agent;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Apps.Chat;

[App(title: "Chat", icon: Icons.MessageSquare, group: ["Apps"], order: Constants.Chat, isVisible: true, allowDuplicateTabs: true)]
public class ChatApp : ViewBase
{
    public override object Build()
    {
        var args = UseArgs<ChatAppArgs>();
        return new ChatWidget(args?.Prompt, args?.SessionId)
            .WithLayout()
            .Full()
            .RemoveParentPadding();
    }
}
