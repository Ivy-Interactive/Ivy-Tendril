using Ivy.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.Linear.LinearPlugin))]

namespace Ivy.Tendril.Plugin.Linear;

public class LinearPlugin : IIvyPlugin
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.Linear",
        Name = "Linear",
        ConfigSectionName = "Linear",
        Version = new Version(1, 0, 0),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new()
    {
        Fields =
        [
            new()
            {
                Key = "ApiKey",
                Type = ConfigFieldType.Secret,
                IsRequired = true,
                Description = "Linear API key (starts with lin_api_)"
            }
        ]
    };

    public void Configure(IIvyPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;

        if (context is not ITendrilPluginContext tendrilContext)
            return;

        // TODO: Register Linear services
    }
}
