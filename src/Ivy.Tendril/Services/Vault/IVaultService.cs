using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ivy.Tendril.Services.Vault;

public interface IVaultService
{
    string GenerateVersionTimestamp();

    Task<List<VaultStatus>> GetVaultsAsync();

    Task<VaultStatus> GetStatusAsync(string? vaultId = null);

    Task<VaultCatalog> GetCatalogAsync(string? vaultId = null);

    Task<List<GitHubAccountOption>> GetGitHubAccountsAndOrgsAsync();

    Task<VaultResult> CreateVaultRepoAsync(string repoName, bool isPrivate = true, string? org = null);

    Task<VaultResult> ConnectVaultAsync(string repoUrl, string? customName = null);

    Task<VaultResult> DisconnectVaultAsync(string? vaultId = null);

    Task<VaultResult> SetAlwaysUpToDateAsync(bool alwaysUpToDate, string? vaultId = null);

    Task<VaultPrResult> PushAndCreatePrAsync(VaultExportRequest request, string? vaultId = null);

    Task<VaultResult> ImportProjectAsync(VaultImportRequest request, string? vaultId = null);

    Task<VaultResult> ImportProjectAsync(string projectName, Dictionary<string, string> localRepoMappings, string? vaultId = null);

    Task<VaultSyncResult> PullLatestAsync(string? vaultId = null);

    event Action? VaultChanged;
}
