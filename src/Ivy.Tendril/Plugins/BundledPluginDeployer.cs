using System.Reflection;

namespace Ivy.Tendril.Plugins;

public static class BundledPluginDeployer
{
    private const string VersionFileName = ".version";
    private const string ResourcePrefix = "Ivy.Tendril.BundledPlugins.";

    private static readonly (string PluginName, string[] Files)[] BundledPlugins =
    [
        ("Ivy.Tendril.Plugin.Slack", ["Ivy.Tendril.Plugin.Slack.dll", "Ivy.Tendril.Plugin.Slack.deps.json"])
    ];

    public static void Deploy(string pluginsDirectory)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var hostVersion = assembly.GetName().Version?.ToString() ?? "0.0.0";
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var (pluginName, files) in BundledPlugins)
        {
            var targetDir = Path.Combine(pluginsDirectory, pluginName);
            var versionFile = Path.Combine(targetDir, VersionFileName);

            if (File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == hostVersion)
                continue;

            var resources = files
                .Select(file => (File: file, Resource: $"{ResourcePrefix}{pluginName}.{file}"))
                .Where(r => resourceNames.Contains(r.Resource))
                .ToList();

            if (resources.Count != files.Length)
                continue;

            Directory.CreateDirectory(targetDir);
            foreach (var (file, resource) in resources)
            {
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var target = File.Create(Path.Combine(targetDir, file));
                stream.CopyTo(target);
            }

            File.WriteAllText(versionFile, hostVersion);
        }
    }
}
