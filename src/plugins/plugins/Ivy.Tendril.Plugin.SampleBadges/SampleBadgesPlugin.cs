using Ivy.Plugins;
using Ivy.Tendril.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.SampleBadges.SampleBadgesPlugin))]

namespace Ivy.Tendril.Plugin.SampleBadges;

/// <summary>
/// Demonstrates adding badge providers to sidebar menu items.
/// Badges show notification counts next to menu items (e.g., unread counts).
/// The count function is called on each render, so it can return dynamic values.
/// </summary>
public class SampleBadgesPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.SampleBadges",
        Title = "Sample Badges",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Bell"),
    };

    public PluginConfigurationSchema? ConfigurationSchema => null;

    public void Configure(ITendrilExtendedPluginContext context)
    {
        context.AddApp(new AppDescriptor
        {
            Id = "sample-notifications",
            Title = "Notifications",
            Icon = Icons.Bell,
            IsVisible = true,
            Group = ["Apps"],
            Order = 95,
            ViewFactory = () => new NotificationsView(context),
        });

        // Badge providers read from the shared counter — updated by the view
        context.AddBadgeProvider("sample-notifications", _ => BadgeCounter.NotificationCount);
        context.AddBadgeProvider("icebox", _ => BadgeCounter.IceboxCount);
    }
}

internal static class BadgeCounter
{
    public static int NotificationCount = 3;
    public static int IceboxCount = 42;
}

public class NotificationsView(ITendrilExtendedPluginContext pluginContext) : ViewBase
{
    public override object Build()
    {
        var notifCount = UseState(() => BadgeCounter.NotificationCount);
        var iceboxCount = UseState(() => BadgeCounter.IceboxCount);

        void UpdateNotif(int delta)
        {
            var val = Math.Max(0, notifCount.Value + delta);
            notifCount.Set(val);
            BadgeCounter.NotificationCount = val;
            pluginContext.InvalidateMenu();
        }

        void UpdateIcebox(int delta)
        {
            var val = Math.Max(0, iceboxCount.Value + delta);
            iceboxCount.Set(val);
            BadgeCounter.IceboxCount = val;
            pluginContext.InvalidateMenu();
        }

        return Layout.Vertical().Padding(4).Gap(4)
            | Text.H1("Badge Demo")
            | Text.Muted("Use the buttons below to change badge counts on sidebar items in real time.")
            | (Layout.Horizontal().Gap(4)
                | new Card(
                    Layout.Vertical().Gap(3)
                        | Text.Label("Notifications Badge")
                        | Text.H2(notifCount.Value.ToString())
                        | (Layout.Horizontal().Gap(2)
                            | new Button("-", onClick: _ => { UpdateNotif(-1); return ValueTask.CompletedTask; })
                            | new Button("+", onClick: _ => { UpdateNotif(1); return ValueTask.CompletedTask; }))
                )
                | new Card(
                    Layout.Vertical().Gap(3)
                        | Text.Label("Icebox Badge")
                        | Text.H2(iceboxCount.Value.ToString())
                        | (Layout.Horizontal().Gap(2)
                            | new Button("-", onClick: _ => { UpdateIcebox(-1); return ValueTask.CompletedTask; })
                            | new Button("+", onClick: _ => { UpdateIcebox(1); return ValueTask.CompletedTask; }))
                ))
            | Callout.Info(
                "Clicking +/- updates a shared counter and calls InvalidateMenu() to signal " +
                "the app shell to re-render the sidebar with updated badge counts.",
                title: "How It Works");
    }
}
