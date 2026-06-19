namespace Ivy.Tendril.AppShell;

internal interface ITendrilPluginContributions
{
    IReadOnlyList<Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>>> SettingsMenuTransformers { get; }
    IReadOnlyList<(string Tag, Func<IServiceProvider, int> CountProvider)> BadgeProviders { get; }
    IReadOnlyDictionary<string, Func<IState<bool>, object?>> DialogFactories { get; }
    event Action<string>? DialogOpenRequested;
    event Action? MenuInvalidated;
}
