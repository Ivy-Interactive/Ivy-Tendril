namespace Ivy.Tendril.Services.Tunnel;

public interface ICloudflaredService
{
    string? TunnelUrl { get; }
    bool IsConnected { get; }
    event Action<string>? TunnelConnected;
    event Action? TunnelDisconnected;
}
