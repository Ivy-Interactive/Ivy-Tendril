using Ivy.Plugins;
using Ivy.Tendril.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.Linear.LinearPlugin))]

namespace Ivy.Tendril.Plugin.Linear;

public class LinearPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.Linear",
        Title = "Linear",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Linear"),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret("ApiKey", description: "Linear API key (starts with lin_api_)", isRequired: true)
        .Build();

    public void Configure(ITendrilExtendedPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;
        var clientFactory = new LinearClientFactory(apiKey);

        var openImportDialog = context.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, context.TendrilHome));

        context.TransformSettingsMenuItems(items =>
        {
            var list = items.ToList();
            var importIndex = list.FindIndex(m => (string?)m.Tag == "$import-issues");
            var insertAt = importIndex >= 0 ? importIndex + 1 : list.Count;
            list.Insert(insertAt,
                MenuItem.Default("Import Issues from Linear")
                    .Tag("$linear-import-issues")
                    .Icon(Icons.Download)
                    .OnSelect(() => openImportDialog()));
            return list;
        });
    }
}
