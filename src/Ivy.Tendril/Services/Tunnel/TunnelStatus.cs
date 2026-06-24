namespace Ivy.Tendril.Services.Tunnel;

public enum TunnelStatus
{
    /// <summary>Tunnel is not running.</summary>
    Disabled,

    /// <summary>Tunnel process is starting and not yet routable. A URL may already exist but is not reachable.</summary>
    Connecting,

    /// <summary>Tunnel is fully established and verified routable.</summary>
    Connected
}
