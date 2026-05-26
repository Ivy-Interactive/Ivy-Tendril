namespace Ivy.Tendril.Services.Tunnel;

public interface ICloudflaredService
{
    string? TunnelUrl { get; }
    bool IsConnected { get; }
    bool IsInstalled { get; }
    event Action<string>? TunnelConnected;
    event Action? TunnelDisconnected;
    Task<bool> CheckInstalledAsync(CancellationToken ct = default);
    Task InstallAsync(CancellationToken ct = default);
    Task ActivateAsync(CancellationToken ct = default);
    void Deactivate();
}
