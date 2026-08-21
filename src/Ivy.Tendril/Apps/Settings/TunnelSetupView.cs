using System.Reactive.Disposables;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Widgets.QRCode;

namespace Ivy.Tendril.Apps.Settings;

public class TunnelSetupView : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var tunnelService = UseService<ICloudflaredService>();
        var shareTunnelService = UseService<IShareTunnelService>();
        var copyToClipboard = UseClipboard();
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);

        var error = UseState<string?>(tunnelService.ErrorMessage);
        var status = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);
        var shareError = UseState<string?>(shareTunnelService.ErrorMessage);
        var shareStatus = UseState(shareTunnelService.Status);
        var shareTunnelUrl = UseState<string?>(shareTunnelService.TunnelUrl);
        var (alertView, showAlert) = UseAlert();

        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                status.Set(newStatus);
                tunnelUrl.Set(tunnelService.TunnelUrl);
                error.Set(tunnelService.ErrorMessage);
            }

            tunnelService.StatusChanged += OnStatusChanged;

            status.Set(tunnelService.Status);
            tunnelUrl.Set(tunnelService.TunnelUrl);
            error.Set(tunnelService.ErrorMessage);

            return Disposable.Create(() => tunnelService.StatusChanged -= OnStatusChanged);
        });

        var form = Layout.Vertical()
                   | Text.Block("Tunnel").Bold()
                   | Text.Block("Expose your Tendril instance to the internet via a Cloudflare tunnel. Useful for accessing Tendril from mobile devices or sharing with others.").Muted().Small();

        if (error.Value is not null && status.Value == TunnelStatus.Disabled)
        {
            form |= Callout.Error(error.Value, "Error");
        }

        if (status.Value == TunnelStatus.Connecting)
        {
            var calloutContent = Layout.Vertical()
                                 | Text.Block(
                                     "Starting tunnel and waiting for it to become routable. This typically takes 15-30 seconds.")
                                 | new Loading()
                                 | new Button("Deactivate").Outline()
                                     .OnClick(async () =>
                                     {
                                         status.Set(TunnelStatus.Disabled);
                                         tunnelUrl.Set(null);
                                         client.Toast("Tunnel stopped", "Deactivated");
                                         await tunnelService.DeactivateAsync();
                                     });
            form |= Callout.Info(calloutContent, "Tunnel Starting");
        }
        else if (status.Value == TunnelStatus.Connected && tunnelUrl.Value is not null)
        {
            var calloutContent = Layout.Vertical()
                                 | "Your tunnel is running and accessible at the URL below."
                                 | Text.Monospaced(tunnelUrl.Value)
                                 | (Layout.Horizontal()
                                    | new Button("Copy URL").Icon(Icons.ClipboardCopy).Outline()
                                        .OnClick(() =>
                                        {
                                            copyToClipboard(tunnelUrl.Value!);
                                            client.Toast("Tunnel URL copied to clipboard", "URL Copied");
                                        })
                                    | new Button("Open in Browser").Icon(Icons.ExternalLink).Outline()
                                        .OnClick(() => client.OpenUrl(tunnelUrl.Value!)))
                                 | new QRCode
                                 {
                                     Value = tunnelUrl.Value,
                                     PixelSize = 200,
                                     ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium
                                 }
                                 | new Button("Deactivate").Outline()
                                     .OnClick(async () =>
                                     {
                                         status.Set(TunnelStatus.Disabled);
                                         tunnelUrl.Set(null);
                                         client.Toast("Tunnel stopped", "Deactivated");
                                         await tunnelService.DeactivateAsync();
                                     });

            form |= new Callout(calloutContent, "Tunnel Active", CalloutVariant.Success);
        }
        else
        {
            form |= new Button("Activate").Primary()
                .OnClick(async () =>
                {
                    error.Set(null);
                    status.Set(TunnelStatus.Connecting);

                    try
                    {
                        var installed = await tunnelService.CheckInstalledAsync();
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
                                        await tunnelService.InstallAsync();
                                        await tunnelService.ActivateAsync();
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

                        await tunnelService.ActivateAsync();
                    }
                    catch (Exception ex)
                    {
                        error.Set($"Failed to start tunnel: {ex.Message}");
                        status.Set(TunnelStatus.Disabled);
                    }
                });
        }

        UseEffect(() =>
        {
            void OnShareStatusChanged(TunnelStatus newStatus)
            {
                shareStatus.Set(newStatus);
                shareTunnelUrl.Set(shareTunnelService.TunnelUrl);
                shareError.Set(shareTunnelService.ErrorMessage);
            }

            shareTunnelService.StatusChanged += OnShareStatusChanged;

            shareStatus.Set(shareTunnelService.Status);
            shareTunnelUrl.Set(shareTunnelService.TunnelUrl);
            shareError.Set(shareTunnelService.ErrorMessage);

            return Disposable.Create(() => shareTunnelService.StatusChanged -= OnShareStatusChanged);
        });

        var shareSection = Layout.Vertical()
            | new Separator()
            | Text.Block("Share Tunnel").Bold()
            | Text.Block("Expose a read-only, comment-only version of Tendril for team members to review plans and drafts.").Muted().Small();

        if (shareError.Value is not null && shareStatus.Value == TunnelStatus.Disabled)
        {
            shareSection |= Callout.Error(shareError.Value, "Error");
        }

        if (shareStatus.Value == TunnelStatus.Connecting)
        {
            var calloutContent = Layout.Vertical()
                | Text.Block("Starting share tunnel and waiting for it to become routable. This typically takes 15-30 seconds.")
                | new Loading()
                | new Button("Deactivate").Outline()
                    .OnClick(async () =>
                    {
                        shareStatus.Set(TunnelStatus.Disabled);
                        shareTunnelUrl.Set(null);
                        client.Toast("Share tunnel stopped", "Deactivated");
                        await shareTunnelService.DeactivateAsync();
                    });
            shareSection |= Callout.Info(calloutContent, "Share Tunnel Starting");
        }
        else if (shareStatus.Value == TunnelStatus.Connected && shareTunnelUrl.Value is not null)
        {
            var shareUrl = $"{shareTunnelUrl.Value.TrimEnd('/')}/review?share=1";
            var calloutContent = Layout.Vertical()
                | "Your share tunnel is running and accessible at the URL below."
                | Text.Monospaced(shareUrl)
                | (Layout.Horizontal()
                    | new Button("Copy URL").Icon(Icons.ClipboardCopy).Outline()
                        .OnClick(() =>
                        {
                            copyToClipboard(shareUrl);
                            client.Toast("Share tunnel URL copied to clipboard", "URL Copied");
                        })
                    | new Button("Open in Browser").Icon(Icons.ExternalLink).Outline()
                        .OnClick(() => client.OpenUrl(shareUrl)))
                | new QRCode
                {
                    Value = shareUrl,
                    PixelSize = 200,
                    ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium
                }
                | new Button("Deactivate").Outline()
                    .OnClick(async () =>
                    {
                        shareStatus.Set(TunnelStatus.Disabled);
                        shareTunnelUrl.Set(null);
                        client.Toast("Share tunnel stopped", "Deactivated");
                        await shareTunnelService.DeactivateAsync();
                    });

            shareSection |= new Callout(calloutContent, "Share Tunnel Active", CalloutVariant.Success);
        }
        else
        {
            shareSection |= new Button("Activate").Primary()
                .OnClick(async () =>
                {
                    shareError.Set(null);
                    shareStatus.Set(TunnelStatus.Connecting);

                    try
                    {
                        var installed = await shareTunnelService.CheckInstalledAsync();
                        if (!installed)
                        {
                            shareStatus.Set(TunnelStatus.Disabled);
                            showAlert("Cloudflare is not installed. Would you like to download and install it?", async result =>
                            {
                                if (result == AlertResult.Ok)
                                {
                                    shareStatus.Set(TunnelStatus.Connecting);
                                    try
                                    {
                                        await shareTunnelService.InstallAsync();
                                        await shareTunnelService.ActivateAsync();
                                    }
                                    catch (Exception ex)
                                    {
                                        shareError.Set($"Failed to install Cloudflare: {ex.Message}");
                                        shareStatus.Set(TunnelStatus.Disabled);
                                    }
                                }
                                else
                                {
                                    shareStatus.Set(TunnelStatus.Disabled);
                                }
                            });
                            return;
                        }

                        await shareTunnelService.ActivateAsync();
                    }
                    catch (Exception ex)
                    {
                        shareError.Set($"Failed to start share tunnel: {ex.Message}");
                        shareStatus.Set(TunnelStatus.Disabled);
                    }
                });
        }

        if (BetaHelper.IsBeta(tendrilArgs, config))
        {
            form |= shareSection;
        }
        form |= alertView;

        return form;
    }
}
