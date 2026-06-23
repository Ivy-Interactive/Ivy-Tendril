namespace Ivy.Tendril.Services.Tunnel;

public interface ICloudflaredService
{
    string? TunnelUrl { get; }
    TunnelStatus Status { get; }
    bool IsConnected { get; }
    bool IsInstalled { get; }
    event Action<TunnelStatus>? StatusChanged;
    Task<bool> CheckInstalledAsync(CancellationToken ct = default);
    Task InstallAsync(CancellationToken ct = default);
    Task ActivateAsync(CancellationToken ct = default);
    Task DeactivateAsync();
}
