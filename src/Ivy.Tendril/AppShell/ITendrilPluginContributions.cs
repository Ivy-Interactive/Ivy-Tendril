namespace Ivy.Tendril.AppShell;

internal interface ITendrilPluginContributions
{
    IReadOnlyList<Func<IEnumerable<MenuItem>, IEnumerable<MenuItem>>> SettingsMenuTransformers { get; }
    IReadOnlyDictionary<string, Func<IState<bool>, object?>> DialogFactories { get; }
    event Action<string>? DialogOpenRequested;
}
