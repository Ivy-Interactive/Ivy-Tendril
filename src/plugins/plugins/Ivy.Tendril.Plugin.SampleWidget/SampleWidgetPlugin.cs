using Ivy.Plugins;
using Ivy.Tendril.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.SampleWidget.SampleWidgetPlugin))]

namespace Ivy.Tendril.Plugin.SampleWidget;

public class SampleWidgetPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.SampleWidget",
        Title = "Sample Widget",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Sparkles"),
    };

    public PluginConfigurationSchema? ConfigurationSchema => null;

    public void Configure(ITendrilExtendedPluginContext context)
    {
        context.AddApp(new AppDescriptor
        {
            Id = "sample-widget-demo",
            Title = "Widget Demo",
            Icon = Icons.Sparkles,
            IsVisible = true,
            Group = ["Apps"],
            Order = 200,
            ViewFactory = () => new SampleWidgetDemoView(),
        });
    }
}
