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
        Icon = PluginIcon.Named("Linear"),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret("ApiKey", description: "Linear API key (starts with lin_api_)", isRequired: true)
        .Build();

    public void Configure(ITendrilExtendedPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;
        var clientFactory = new LinearClientFactory(apiKey);

        // Lets Tendril label Linear-sourced plans (PR bodies, inbox filenames) without knowing
        // Linear's URL format. Also covers URLs that never came through the import dialog — a
        // pasted link, or one an agent found in a task description.
        context.SourceLinks.RegisterResolver(LinearSourceUrl.GetIdentifier);

        var openImportDialog = context.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, context.Inbox));

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
