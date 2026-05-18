using Ivy.Tendril.Plugins;

namespace Ivy.Tendril.AppShell;

internal interface ISettingsMenuItemsProvider
{
    IReadOnlyList<(MenuItem Item, FooterMenuPosition Position)> SettingsMenuItems { get; }
}
