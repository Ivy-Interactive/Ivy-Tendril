using Ivy.Plugins;

namespace Ivy.Tendril.Plugins;

public static class TendrilPluginContextExtensions
{
    /// <summary>
    /// Casts the plugin context to <see cref="ITendrilExtendedPluginContext"/>.
    /// Throws if the context is not a Tendril extended context.
    /// </summary>
    public static ITendrilExtendedPluginContext AsTendrilExtendedContext(this IIvyPluginContext context)
    {
        return context as ITendrilExtendedPluginContext
            ?? throw new InvalidOperationException(
                $"The plugin context does not implement {nameof(ITendrilExtendedPluginContext)}. " +
                "Ensure you are running inside Ivy Tendril.");
    }

    /// <summary>
    /// Attempts to cast the plugin context to <see cref="ITendrilExtendedPluginContext"/>.
    /// Returns null if the context is not a Tendril extended context.
    /// </summary>
    public static ITendrilExtendedPluginContext? TryGetTendrilExtendedContext(this IIvyPluginContext context)
    {
        return context as ITendrilExtendedPluginContext;
    }
}
