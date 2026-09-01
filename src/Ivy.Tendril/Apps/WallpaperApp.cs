using System;
using System.Threading.Tasks;
using System.Reactive.Disposables;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Services.Tunnel;
using Ivy.Widgets.ActivityHeatmap;
using Ivy.Widgets.QRCode;

namespace Ivy.Tendril.Apps;

[App(isVisible: false)]
public class WallpaperApp : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var planDbService = UseService<IPlanDatabaseService>();
        var tunnelService = UseService<ICloudflaredService>();
        var shareTunnelService = UseService<IShareTunnelService>();
        var copyToClipboard = UseClipboard();
        Context.TryUseService<TendrilArgs>(out var tendrilArgs);
        var tunnelStatus = UseState(tunnelService.Status);
        var tunnelUrl = UseState<string?>(tunnelService.TunnelUrl);
        var shareTunnelStatus = UseState(shareTunnelService.Status);
        var shareTunnelUrl = UseState<string?>(shareTunnelService.TunnelUrl);

        var processView = Context.UseTendrilProcess();

        UseEffect(() =>
        {
            void OnStatusChanged(TunnelStatus newStatus)
            {
                tunnelStatus.Set(newStatus);
                tunnelUrl.Set(tunnelService.TunnelUrl);
            }

            tunnelService.StatusChanged += OnStatusChanged;

            tunnelStatus.Set(tunnelService.Status);
            tunnelUrl.Set(tunnelService.TunnelUrl);

            return Disposable.Create(() => tunnelService.StatusChanged -= OnStatusChanged);
        });

        UseEffect(() =>
        {
            void OnShareStatusChanged(TunnelStatus newStatus)
            {
                shareTunnelStatus.Set(newStatus);
                shareTunnelUrl.Set(shareTunnelService.TunnelUrl);
            }

            shareTunnelService.StatusChanged += OnShareStatusChanged;

            shareTunnelStatus.Set(shareTunnelService.Status);
            shareTunnelUrl.Set(shareTunnelService.TunnelUrl);

            return Disposable.Create(() => shareTunnelService.StatusChanged -= OnShareStatusChanged);
        });

        // Query last 90 days of completed PRs
        var prData = planDbService.GetCompletedPrsByDay(90);
        var activities = prData.Select(x => new Activity { Date = x.Date, Count = x.Count }).ToArray();

        // Build vertical layout conditionally including heatmap only if there are PRs
        var verticalLayout = Layout.Vertical().Gap(2).AlignContent(Align.Center)
            | new Image("/tendril/assets/Tendril.svg").Width(Size.Units(30)).Height(Size.Auto())
            | Text.H2("What are we making next?")
            | processView;

        if (activities.Length > 0)
        {
            verticalLayout |= new Spacer().Height(Size.Units(5));
            verticalLayout |= new ActivityHeatmap()
                .Data(activities)
                .ShowMonthLabels(true)
                .ShowDayLabels(true)
                .StartDate(DateOnly.FromDateTime(DateTime.Today.AddDays(-89)))
                .EndDate(DateOnly.FromDateTime(DateTime.Today))
                .ValueLabel("PRs");
        }

        var elements = new List<object>
        {
            Layout.Center() | verticalLayout
        };

        // Only show the tunnel QR once the tunnel is fully established and routable
        // (Status == Connected), never during the Connecting phase.
        if (tunnelStatus.Value == TunnelStatus.Connected && tunnelUrl.Value is { } tunnelAddress)
        {
            var tunnelMenu = TunnelUiHelper.BuildTunnelMenu(client, copyToClipboard, tunnelAddress, () =>
            {
                // Optimistically remove the panel; the teardown runs in the background.
                tunnelStatus.Set(TunnelStatus.Disabled);
                client.Toast("Tunnel stopped", "Deactivated");
                _ = tunnelService.DeactivateAsync();
            });

            var tunnelQr = new FloatingPanel(
                new Card(
                    new QRCode { Value = tunnelAddress, PixelSize = 160, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium }
                ).Header("Tunnel", null, tunnelMenu)
            )
            .AlignSelf(Align.TopRight)
            .Offset(new Thickness(0, 8, 8, 0))
            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

            elements.Add(tunnelQr);
        }

        var isBeta = BetaHelper.IsBeta(tendrilArgs, config);

        if (isBeta && shareTunnelStatus.Value == TunnelStatus.Connected && shareTunnelUrl.Value is not null)
        {
            var shareAddress = shareTunnelUrl.Value;
            var shareMenu = new Button().Icon(Icons.Ellipsis).Ghost().Small().WithDropDown(
                new MenuItem("Copy Share URL", Icon: Icons.ClipboardCopy, Tag: "copy").OnSelect(() =>
                {
                    copyToClipboard(shareAddress);
                    client.Toast("Share tunnel URL copied to clipboard", "URL Copied");
                }),
                new MenuItem("Open in Browser", Icon: Icons.ExternalLink, Tag: "open").OnSelect(() => client.OpenUrl(shareAddress)),
                new MenuItem("Deactivate", Icon: Icons.Power, Tag: "deactivate").OnSelect(() =>
                {
                    shareTunnelStatus.Set(TunnelStatus.Disabled);
                    client.Toast("Share tunnel stopped", "Deactivated");
                    _ = shareTunnelService.DeactivateAsync();
                })
            );

            var topOffset = tunnelStatus.Value == TunnelStatus.Connected ? 240 : 8;

            var shareQr = new FloatingPanel(
                new Card(
                    new QRCode { Value = shareAddress, PixelSize = 160, ErrorCorrectionLevel = QrErrorCorrectionLevel.Medium }
                ).Header("Share Tunnel", null, shareMenu)
            )
            .AlignSelf(Align.TopRight)
            .Offset(new Thickness(0, topOffset, 8, 0))
            .HideOn(Breakpoint.Mobile, Breakpoint.Tablet);

            elements.Add(shareQr);
        }

        elements.Add(new UpdateNoticeView(floating: true));

        return new Fragment(elements.ToArray());
    }
}
