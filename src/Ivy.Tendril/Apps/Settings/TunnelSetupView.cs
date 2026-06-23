using System.Reactive.Disposables;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Widgets.QRCode;

namespace Ivy.Tendril.Apps.Settings;

public class TunnelSetupView : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var tunnelService = UseService<ICloudflaredService>();
        var copyToClipboard = UseClipboard();

        var error = UseState<string?>(null);
        var status = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);
        var (alertView, showAlert) = UseAlert();

        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                status.Set(newStatus);
                tunnelUrl.Set(tunnelService.TunnelUrl);
            }

            tunnelService.StatusChanged += OnStatusChanged;

            status.Set(tunnelService.Status);
            tunnelUrl.Set(tunnelService.TunnelUrl);

            return Disposable.Create(() => tunnelService.StatusChanged -= OnStatusChanged);
        });

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Tunnel").Bold()
                   | Text.Block("Expose your Tendril instance to the internet via a Cloudflare tunnel. Useful for accessing Tendril from mobile devices or sharing with others.").Muted().Small();

        if (error.Value is not null)
        {
            form |= Callout.Error(error.Value, "Error");
        }

        if (status.Value == TunnelStatus.Connecting)
        {
            form |= Callout.Info("Starting tunnel and waiting for it to become routable. This typically takes 15-30 seconds.", "Tunnel Starting");
            form |= new Loading();
        }
        else if (status.Value == TunnelStatus.Connected && tunnelUrl.Value is not null)
        {
            form |= Callout.Success("Your tunnel is running and accessible at the URL below.", "Tunnel Active");
            form |= Text.Block("Tunnel URL").Bold().Small();
            form |= Text.Monospaced(tunnelUrl.Value);
            form |= Layout.Horizontal()
                   | new Button("Copy URL").Icon(Icons.ClipboardCopy).Outline()
                       .OnClick(() =>
                       {
                           copyToClipboard(tunnelUrl.Value!);
                           client.Toast("Tunnel URL copied to clipboard", "URL Copied");
                       })
                   | new Button("Open in Browser").Icon(Icons.ExternalLink).Outline()
                       .OnClick(() => client.OpenUrl(tunnelUrl.Value!));
            form |= Text.Block("Scan QR Code").Bold().Small();
            form |= new QRCode { Value = tunnelUrl.Value, PixelSize = 200, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium };
            form |= new Button("Deactivate").Secondary()
                .OnClick(async () =>
                {
                    // Optimistically flip to the disconnected view immediately;
                    // the actual teardown runs in the background below.
                    status.Set(TunnelStatus.Disabled);
                    tunnelUrl.Set(null);
                    client.Toast("Tunnel stopped", "Deactivated");
                    await tunnelService.DeactivateAsync();
                });
        }
        else
        {
            form |= new Button("Activate").Primary()
                .OnClick(async () =>
                {
                    error.Set(null);
                    // Optimistically show the connecting state; the service drives
                    // it to Connected (or back to Disabled on failure) via StatusChanged.
                    status.Set(TunnelStatus.Connecting);

                    try
                    {
                        var installed = await tunnelService.CheckInstalledAsync();
                        if (!installed)
                        {
                            status.Set(TunnelStatus.Disabled);
                            showAlert("Cloudflared is not installed. Would you like to download and install it?", async result =>
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
                                        error.Set($"Failed to install cloudflared: {ex.Message}");
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

        form |= alertView;

        return form;
    }
}
