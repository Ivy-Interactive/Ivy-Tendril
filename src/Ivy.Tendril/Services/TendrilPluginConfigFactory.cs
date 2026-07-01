using Ivy.Plugins;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services;

internal class TendrilPluginConfigFactory(string pluginsDir) : IIvyPluginConfigFactory
{
    private readonly string _configPath = Path.Combine(pluginsDir, "plugin-config.yaml");
    private IPluginManager? _pluginManager;

    public void SetPluginManager(IPluginManager pluginManager) => _pluginManager = pluginManager;

    public IIvyPluginConfig Create(string pluginId) =>
        new TendrilPluginConfig(_configPath, pluginId, () => _pluginManager);
}

internal class TendrilPluginConfig(string configPath, string pluginId, Func<IPluginManager?> getPluginManager) : IIvyPluginConfig
{
    public string? GetValue(string key)
    {
        var data = Load();
        if (data.TryGetValue(pluginId, out var section) && section is Dictionary<object, object> dict)
            return dict.TryGetValue(key, out var value) ? value?.ToString() : null;
        return null;
    }

    public void SetValue(string key, string value)
    {
        var data = Load();
        var section = GetOrCreateSection(data);
        section[key] = value;
        Save(data);
    }

    public void RemoveValue(string key)
    {
        var data = Load();
        if (data.TryGetValue(pluginId, out var section) && section is Dictionary<object, object> dict)
        {
            dict.Remove(key);
            Save(data);
        }
    }

    public void Save()
    {
        getPluginManager()?.ReconfigurePlugin(pluginId);
    }

    private Dictionary<object, object> GetOrCreateSection(Dictionary<object, object> data)
    {
        if (data.TryGetValue(pluginId, out var existing) && existing is Dictionary<object, object> dict)
            return dict;
        var section = new Dictionary<object, object>();
        data[pluginId] = section;
        return section;
    }

    private Dictionary<object, object> Load()
    {
        if (File.Exists(configPath))
        {
            var yaml = File.ReadAllText(configPath);
            if (!string.IsNullOrWhiteSpace(yaml))
                return YamlHelper.Deserializer.Deserialize<Dictionary<object, object>>(yaml)
                       ?? new Dictionary<object, object>();
        }
        return new Dictionary<object, object>();
    }

    private void Save(Dictionary<object, object> data)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);
        var yaml = YamlHelper.Serializer.Serialize(data);
        File.WriteAllText(configPath, yaml);
    }
}
