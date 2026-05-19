using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Setup;

public class NotificationsSetupView : ViewBase
{
    public override object Build()
    {
        var config = UseService<IConfigService>();
        var client = UseService<IClientProvider>();

        var desktopNotifications = UseState(config.Settings.DesktopNotifications);
        var forceRender = UseState(0);

        var hasChanges = desktopNotifications.Value != config.Settings.DesktopNotifications;

        var form = Layout.Vertical().Gap(4).Padding(4).Width(Size.Auto().Max(Size.Units(120)))
                   | Text.Block("Notification Settings").Bold()
                   | Text.Block("Configure how Tendril notifies you about job completions, failures, and other events.").Muted().Small()

                   | desktopNotifications.ToSwitchInput()
                       .WithField().Label("Enable Desktop Notifications")
                   | new Button("Save").Primary()
                       .Disabled(!hasChanges)
                       .OnClick(() =>
                       {
                           config.Settings.DesktopNotifications = desktopNotifications.Value;
                           config.SaveSettings();
                           client.Toast("Notification settings saved", "Saved");
                           forceRender.Value++;
                       });

        return form;
    }
}
