using Ivy.Tendril.Plugins;

namespace Ivy.Tendril.AppShell;

internal interface ITendrilPluginContributions
{
    IReadOnlyList<(MenuItem Item, FooterMenuPosition Position)> SettingsMenuItems { get; }
    IReadOnlyDictionary<string, Func<IState<bool>, object?>> DialogFactories { get; }
    event Action<string>? DialogOpenRequested;
}
