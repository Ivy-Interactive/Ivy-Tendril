namespace Ivy.Tendril.Services.Tunnel;

public interface IShareTunnelService
{
    string? TunnelUrl { get; }
    TunnelStatus Status { get; }
    bool IsConnected { get; }
    bool IsInstalled { get; }
    string? ErrorMessage { get; }
    int SharePort { get; }
    event Action<TunnelStatus>? StatusChanged;
    Task<bool> CheckInstalledAsync(CancellationToken ct = default);
    Task InstallAsync(CancellationToken ct = default);
    Task ActivateAsync(CancellationToken ct = default);
    Task DeactivateAsync();
    string GetShareUrlForPlan(string planFolderName, bool isReview = true);
}
