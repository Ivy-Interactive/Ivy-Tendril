using Ivy.Core.Plugins;
using Ivy.Plugins;
using Ivy.Plugins.Messaging;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril;

internal class TendrilPluginContext(Server server, WebApplicationBuilder builder)
    : PluginContextBase(server, builder), ITendrilPluginContext, ITendrilExtendedPluginContext, ITendrilPluginContributions
{
    private readonly List<(MenuItem Item, FooterMenuPosition Position)> _settingsMenuItems = [];
    private readonly Dictionary<string, Func<IState<bool>, object?>> _dialogFactories = [];

    public IReadOnlyList<(MenuItem Item, FooterMenuPosition Position)> SettingsMenuItems => _settingsMenuItems;
    public IReadOnlyDictionary<string, Func<IState<bool>, object?>> DialogFactories => _dialogFactories;

    public event Action<string>? DialogOpenRequested;

    public void AddSettingsMenuItem(MenuItem item, FooterMenuPosition position)
    {
        if (item.Tag is null)
            throw new InvalidOperationException(
                "Settings menu items contributed by plugins must have a Tag set for stable ordering.");

        _settingsMenuItems.Add((item, position));
    }

    public Action RegisterDialog(string id, Func<IState<bool>, object?> factory)
    {
        _dialogFactories[id] = factory;
        return () => DialogOpenRequested?.Invoke(id);
    }
}
