using System;

namespace Ivy.Tendril.Helpers;

public static class TunnelUiHelper
{
    public static DropDownMenu BuildTunnelMenu(
        IClientProvider client,
        Action<string> copyToClipboard,
        string tunnelAddress,
        Action deactivate)
    {
        return new Button().Icon(Icons.Ellipsis).Ghost().Small().WithDropDown(
            new MenuItem("Copy to Clipboard", Icon: Icons.ClipboardCopy, Tag: "copy").OnSelect(() =>
            {
                copyToClipboard(tunnelAddress);
                client.Toast("Tunnel URL copied to clipboard", "URL Copied");
            }),
            new MenuItem("Open in Browser", Icon: Icons.ExternalLink, Tag: "open").OnSelect(() => client.OpenUrl(tunnelAddress)),
            new MenuItem("Deactivate", Icon: Icons.Power, Tag: "deactivate").OnSelect(deactivate));
    }
}
