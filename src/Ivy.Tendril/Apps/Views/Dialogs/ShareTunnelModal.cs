using System.Reactive.Disposables;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Widgets.QRCode;

namespace Ivy.Tendril.Apps.Views.Dialogs;

public class ShareTunnelModal(
    IState<bool> dialogOpen,
    string? planFolderName = null,
    bool isReview = true) : ViewBase
{
    public override object? Build()
    {
        var client = UseService<IClientProvider>();
        var shareTunnelService = UseService<IShareTunnelService>();
        var copyToClipboard = UseClipboard();

        var status = UseState(shareTunnelService.Status);
        var tunnelUrl = UseState<string?>(shareTunnelService.TunnelUrl);
        var error = UseState<string?>(shareTunnelService.ErrorMessage);
        var (alertView, showAlert) = UseAlert();

        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                status.Set(newStatus);
                tunnelUrl.Set(shareTunnelService.TunnelUrl);
                error.Set(shareTunnelService.ErrorMessage);
            }

            shareTunnelService.StatusChanged += OnStatusChanged;
            status.Set(shareTunnelService.Status);
            tunnelUrl.Set(shareTunnelService.TunnelUrl);
            error.Set(shareTunnelService.ErrorMessage);

            return Disposable.Create(() => shareTunnelService.StatusChanged -= OnStatusChanged);
        });

        if (!dialogOpen.Value) return null;

        var targetUrl = !string.IsNullOrEmpty(planFolderName)
            ? shareTunnelService.GetShareUrlForPlan(planFolderName, isReview)
            : tunnelUrl.Value ?? shareTunnelService.TunnelUrl;

        var content = Layout.Vertical()
            | Text.P("Share your work with teammates via a secure, read-only Cloudflare tunnel. Reviewers can inspect plans, diffs, and leave comments without having access to your terminal or settings.");

        if (error.Value is not null)
        {
            content |= Callout.Error(error.Value, "Error");
        }

        if (status.Value == TunnelStatus.Connecting)
        {
            var callout = Layout.Vertical()
                | Text.Block("Starting share tunnel... This typically takes 15-30 seconds.")
                | new Loading();
            content |= Callout.Info(callout, "Tunnel Starting");
        }
        else if (status.Value == TunnelStatus.Connected && targetUrl is not null)
        {
            var callout = Layout.Vertical()
                | Text.Block("Your share tunnel is active with read-only & comment-only permissions.")
                | Text.Monospaced(targetUrl)
                | (Layout.Horizontal()
                    | new Button("Copy Link").Icon(Icons.ClipboardCopy).Outline()
                        .OnClick(() =>
                        {
                            copyToClipboard(targetUrl);
                            client.Toast("Share URL copied to clipboard", "Link Copied");
                        })
                    | new Button("Open in Browser").Icon(Icons.ExternalLink).Outline()
                        .OnClick(() => client.OpenUrl(targetUrl)))
                | new QRCode
                {
                    Value = targetUrl,
                    PixelSize = 180,
                    ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium
                }
                | new Button("Stop Sharing").Outline()
                    .OnClick(async () =>
                    {
                        status.Set(TunnelStatus.Disabled);
                        tunnelUrl.Set(null);
                        client.Toast("Share tunnel stopped", "Deactivated");
                        await shareTunnelService.DeactivateAsync();
                    });

            content |= new Callout(callout, "Share Active", CalloutVariant.Success);
        }
        else
        {
            content |= new Button("Start Share Tunnel").Primary()
                .OnClick(async () =>
                {
                    error.Set(null);
                    status.Set(TunnelStatus.Connecting);

                    try
                    {
                        var installed = await shareTunnelService.CheckInstalledAsync();
                        if (!installed)
                        {
                            status.Set(TunnelStatus.Disabled);
                            showAlert("Cloudflare is not installed. Would you like to download and install it?", async result =>
                            {
                                if (result == AlertResult.Ok)
                                {
                                    status.Set(TunnelStatus.Connecting);
                                    try
                                    {
                                        await shareTunnelService.InstallAsync();
                                        await shareTunnelService.ActivateAsync();
                                    }
                                    catch (Exception ex)
                                    {
                                        error.Set($"Failed to install Cloudflare: {ex.Message}");
                                        status.Set(TunnelStatus.Disabled);
                                    }
                                }
                                else
                                {
                                    status.Set(TunnelStatus.Disabled);
                                }
                            });
                            return;
                        }

                        await shareTunnelService.ActivateAsync();
                    }
                    catch (Exception ex)
                    {
                        error.Set($"Failed to start share tunnel: {ex.Message}");
                        status.Set(TunnelStatus.Disabled);
                    }
                });
        }

        content |= alertView;

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Share Work"),
            new DialogBody(content)
        ).Width(Size.Rem(32));
    }
}
