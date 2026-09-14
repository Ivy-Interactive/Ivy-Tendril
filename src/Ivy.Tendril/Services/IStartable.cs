namespace Ivy.Tendril.Services;

/// <summary>
/// Marker interface for services that require an explicit Start() call after DI resolution.
/// Services implementing this will be automatically started by BackgroundServiceActivator.
/// </summary>
public interface IStartable
{
    void Start();
}

/// <summary>
/// A background service that must run on the master instance only, and must stop if this
/// instance loses mastership.
/// </summary>
/// <remarks>
/// Registered as <c>IMasterOnlyStartable</c> rather than <c>IStartable</c>, which is what keeps a
/// non-master from ever resolving it: Microsoft DI resolves by exact registered type, so a service
/// in this collection is not constructed at all unless BackgroundServiceActivator asks for it after
/// the election has run.
/// </remarks>
public interface IMasterOnlyStartable : IStartable
{
    void Stop();
}
