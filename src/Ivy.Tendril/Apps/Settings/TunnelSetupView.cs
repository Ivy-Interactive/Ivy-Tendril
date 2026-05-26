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

        var isLoading = UseState(false);
        var error = UseState<string?>(null);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);
        var isConnected = UseState(tunnelService.IsConnected);
        var (alertView, showAlert) = UseAlert();

        UseEffect(() =>
        {
            void OnConnected(string url)
            {
                tunnelUrl.Set(url);
                isConnected.Set(true);
                isLoading.Set(false);
            }

            void OnDisconnected()
            {
                tunnelUrl.Set(null);
                isConnected.Set(false);
            }

            tunnelService.TunnelConnected += OnConnected;
            tunnelService.TunnelDisconnected += OnDisconnected;

            tunnelUrl.Set(tunnelService.TunnelUrl);
            isConnected.Set(tunnelService.IsConnected);

            return Disposable.Create(() =>
            {
                tunnelService.TunnelConnected -= OnConnected;
                tunnelService.TunnelDisconnected -= OnDisconnected;
            });
        });

        var form = Layout.Vertical().Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Tunnel").Bold()
                   | Text.Block("Expose your Tendril instance to the internet via a Cloudflare tunnel. Useful for accessing Tendril from mobile devices or sharing with others.").Muted().Small();

        if (error.Value is not null)
        {
            form |= Callout.Error(error.Value, "Error");
        }

        if (isConnected.Value && tunnelUrl.Value is not null)
        {
            form |= Callout.Success("Your tunnel is running and accessible at the URL below.", "Tunnel Active");
            form |= Text.Block("Tunnel URL").Bold().Small();
            form |= Text.Monospaced(tunnelUrl.Value);
            form |= Text.Block("Scan QR Code").Bold().Small();
            form |= new QRCode { Value = tunnelUrl.Value, PixelSize = 200, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium };
            form |= new Button("Deactivate").Secondary()
                .OnClick(() =>
                {
                    tunnelService.Deactivate();
                    isConnected.Set(false);
                    tunnelUrl.Set(null);
                    client.Toast("Tunnel stopped", "Deactivated");
                });
        }
        else
        {
            form |= new Button("Activate").Primary()
                .Loading(isLoading.Value)
                .Disabled(isLoading.Value)
                .OnClick(async () =>
                {
                    isLoading.Set(true);
                    error.Set(null);

                    try
                    {
                        var installed = await tunnelService.CheckInstalledAsync();
                        if (!installed)
                        {
                            isLoading.Set(false);
                            showAlert("Cloudflared is not installed. Would you like to download and install it?", async result =>
                            {
                                if (result == AlertResult.Ok)
                                {
                                    isLoading.Set(true);
                                    try
                                    {
                                        await tunnelService.InstallAsync();
                                        await tunnelService.ActivateAsync();
                                    }
                                    catch (Exception ex)
                                    {
                                        error.Set($"Failed to install cloudflared: {ex.Message}");
                                        isLoading.Set(false);
                                    }
                                }
                            });
                            return;
                        }

                        await tunnelService.ActivateAsync();
                    }
                    catch (Exception ex)
                    {
                        error.Set($"Failed to start tunnel: {ex.Message}");
                        isLoading.Set(false);
                    }
                });
        }

        form |= alertView;

        return form;
    }
}
