using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Vault;

public interface IVaultService
{
    string GenerateVersionTimestamp();

    Task<VaultStatus> GetStatusAsync();

    Task<VaultCatalog> GetCatalogAsync();

    Task<VaultResult> CreateVaultRepoAsync(string repoName, bool isPrivate = true, string? org = null);

    Task<VaultResult> ConnectVaultAsync(string repoUrl);

    Task<VaultResult> DisconnectVaultAsync();

    Task<VaultResult> SetAlwaysUpToDateAsync(bool alwaysUpToDate);

    Task<VaultPrResult> PushAndCreatePrAsync(VaultExportRequest request);

    Task<VaultResult> ImportProjectAsync(string projectName, Dictionary<string, string> localRepoMappings);

    Task<VaultSyncResult> PullLatestAsync();

    event Action? VaultChanged;
}
