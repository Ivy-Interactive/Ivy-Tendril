using System.Text.Json;
using Ivy.Plugins;

namespace Ivy.Tendril.Plugins;

public class TendrilPluginConfigFactory(string tendrilHome) : IIvyPluginConfigFactory
{
    private IPluginManager? _pluginManager;

    internal string ConfigDirectory { get; } = Path.Combine(tendrilHome, "plugin-config");

    public event Action<string>? ConfigSaved;

    public void SetPluginManager(IPluginManager pluginManager) => _pluginManager = pluginManager;

    public IIvyPluginConfig Create(string pluginId) =>
        new TendrilPluginConfig(ConfigDirectory, pluginId, () => _pluginManager, () => ConfigSaved?.Invoke(pluginId));
}

internal class TendrilPluginConfig : IIvyPluginConfig
{
    private readonly string _filePath;
    private readonly string _pluginId;
    private readonly Func<IPluginManager?> _pluginManager;
    private readonly Action _onSaved;
    private readonly Lock _lock = new();
    private Dictionary<string, string> _values;

    public TendrilPluginConfig(string configDirectory, string pluginId, Func<IPluginManager?> pluginManager, Action? onSaved = null)
    {
        _pluginId = pluginId;
        _pluginManager = pluginManager;
        _onSaved = onSaved ?? (() => { });
        _filePath = Path.Combine(configDirectory, SanitizeFileName(pluginId) + ".json");
        _values = Load();
    }

    public string? GetValue(string key)
    {
        lock (_lock)
            return _values.TryGetValue(key, out var value) ? value : null;
    }

    public void SetValue(string key, string value)
    {
        lock (_lock)
            _values[key] = value;
    }

    public void RemoveValue(string key)
    {
        lock (_lock)
            _values.Remove(key);
    }

    public void Save()
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, _filePath, overwrite: true);
        }
        _pluginManager()?.ReconfigurePlugin(_pluginId);
        _onSaved();
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath))
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string SanitizeFileName(string pluginId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(pluginId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
