using Ivy.Core.Plugins;
using Ivy.Plugins;
using Ivy.Plugins.Messaging;
using Ivy.Tendril.AppShell;
using Ivy.Tendril.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Ivy.Tendril;

internal class TendrilPluginContext(Server server, WebApplicationBuilder builder, string tendrilHome)
    : PluginContextBase(server, builder), ITendrilExtendedPluginContext, ITendrilPluginContributions
{
    public string TendrilHome { get; } = tendrilHome;
    private readonly List<(Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>> Transformer, int Priority, string PluginId)> _settingsMenuTransformers = [];
    private readonly Dictionary<string, Func<IState<bool>, object?>> _dialogFactories = [];
    private readonly Dictionary<string, string> _dialogOwners = []; // dialog id -> plugin id

    public IReadOnlyList<Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>>> SettingsMenuTransformers =>
        _settingsMenuTransformers
            .OrderBy(x => x.Priority)
            .Select(x => x.Transformer)
            .ToList();

    public IReadOnlyDictionary<string, Func<IState<bool>, object?>> DialogFactories => _dialogFactories;

    public event Action<string>? DialogOpenRequested;

    public void AddSettingsMenuItems(Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>> transformer, int priority = 0)
    {
        var pluginId = CurrentPluginId ?? "__unknown__";
        _settingsMenuTransformers.Add((transformer, priority, pluginId));
    }

    public Action RegisterDialog(string id, Func<IState<bool>, object?> factory)
    {
        var pluginId = CurrentPluginId ?? "__unknown__";
        _dialogFactories[id] = factory;
        _dialogOwners[id] = pluginId;
        return () => DialogOpenRequested?.Invoke(id);
    }

    internal override void RemovePluginContributions(string pluginId)
    {
        base.RemovePluginContributions(pluginId);

        _settingsMenuTransformers.RemoveAll(x => x.PluginId == pluginId);

        var dialogsToRemove = _dialogOwners
            .Where(kv => kv.Value == pluginId)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in dialogsToRemove)
        {
            _dialogFactories.Remove(id);
            _dialogOwners.Remove(id);
        }
    }
}
