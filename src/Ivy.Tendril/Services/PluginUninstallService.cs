using Ivy.Core.Plugins;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Services;

internal enum PluginInstallationType
{
    NuGet,
    Referenced,
    Unknown
}

internal class PluginUninstallService(string pluginsDir)
{
    private readonly string _referencesFilePath = Path.Combine(pluginsDir, PluginReferencesWatcher.FileName);
    private readonly string _configFilePath = Path.Combine(pluginsDir, "plugin-config.yaml");

    public PluginInstallationType GetInstallationType(string pluginDirectory)
    {
        var normalizedDir = Path.GetFullPath(pluginDirectory);

        // Check if it's a referenced plugin
        var referencedPaths = PluginReferencesWatcher.ParseReferencesFile(_referencesFilePath, pluginsDir);
        if (referencedPaths.Any(r =>
                string.Equals(Path.GetFullPath(r), normalizedDir, StringComparison.OrdinalIgnoreCase)))
            return PluginInstallationType.Referenced;

        // Check if it's a direct subdirectory of the plugins folder (NuGet-installed)
        var normalizedPluginsDir = Path.GetFullPath(pluginsDir);
        var parent = Path.GetDirectoryName(normalizedDir);
        if (parent is not null &&
            string.Equals(Path.GetFullPath(parent), normalizedPluginsDir, StringComparison.OrdinalIgnoreCase))
            return PluginInstallationType.NuGet;

        return PluginInstallationType.Unknown;
    }

    public void UninstallNuGetPlugin(string pluginDirectory)
    {
        if (Directory.Exists(pluginDirectory))
            Directory.Delete(pluginDirectory, recursive: true);
    }

    public void UninstallReferencedPlugin(string pluginDirectory)
    {
        if (!File.Exists(_referencesFilePath)) return;

        var lines = File.ReadAllLines(_referencesFilePath);
        var normalizedTarget = Path.GetFullPath(pluginDirectory);
        var normalizedPluginsDir = Path.GetFullPath(pluginsDir);

        var filteredLines = lines.Where(line =>
        {
            var trimmed = line.TrimStart('-', ' ');
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                return true;

            var resolved = Path.IsPathRooted(trimmed)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(trimmed, normalizedPluginsDir);

            return !string.Equals(resolved, normalizedTarget, StringComparison.OrdinalIgnoreCase);
        }).ToArray();

        File.WriteAllLines(_referencesFilePath, filteredLines);
    }

    public void CleanupPluginConfig(string pluginId)
    {
        if (!File.Exists(_configFilePath)) return;

        var yaml = File.ReadAllText(_configFilePath);
        if (string.IsNullOrWhiteSpace(yaml)) return;

        var data = YamlHelper.Deserializer.Deserialize<Dictionary<object, object>>(yaml);
        if (data is null || !data.Remove(pluginId)) return;

        var newYaml = data.Count > 0
            ? YamlHelper.Serializer.Serialize(data)
            : string.Empty;
        File.WriteAllText(_configFilePath, newYaml);
    }
}
