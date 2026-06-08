using Ivy.Plugins;
using Ivy.Tendril.Plugins;

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
        Icon = PluginIcon.Named("Linear"),
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

        if (context is not ITendrilExtendedPluginContext tendrilContext)
            return;

        var clientFactory = new LinearClientFactory(apiKey);

        var openImportDialog = tendrilContext.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, tendrilContext.TendrilHome));

        tendrilContext.AddSettingsMenuItem(
            MenuItem.Default("Import Issues from Linear")
                .Tag("$linear-import-issues")
                .Icon(Icons.Download)
                .OnSelect(() => openImportDialog()),
            FooterMenuPosition.ImportIssues);
    }
}
