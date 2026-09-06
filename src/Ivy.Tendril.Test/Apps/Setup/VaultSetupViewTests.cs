using System;
using System.Collections.Generic;
using Ivy.Tendril.Apps.Settings;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Setup;

public class VaultSetupViewTests
{
    [Fact]
    public void HasConfiguredVaults_WithConfiguredVaults_ReturnsTrue()
    {
        // 1. Settings with enabled vault in Vaults list
        var settingsWithVaultsList = new TendrilSettings
        {
            Vaults = new List<VaultSettings>
            {
                new()
                {
                    Id = "vault-1",
                    Name = "Team Vault",
                    Enabled = true,
                    RepoUrl = "https://github.com/org/vault.git"
                }
            }
        };

        Assert.True(VaultSetupView.HasConfiguredVaults(settingsWithVaultsList));

        // 2. Settings with enabled single Vault (legacy or singular fallback)
        var settingsWithSingleVault = new TendrilSettings
        {
            Vaults = new List<VaultSettings>(),
            Vault = new VaultSettings
            {
                Id = "vault-single",
                Name = "Single Vault",
                Enabled = true,
                RepoUrl = "https://github.com/org/single-vault.git"
            }
        };

        Assert.True(VaultSetupView.HasConfiguredVaults(settingsWithSingleVault));
    }

    [Fact]
    public void HasConfiguredVaults_WithoutConfiguredVaults_ReturnsFalse()
    {
        // Null settings
        Assert.False(VaultSetupView.HasConfiguredVaults(null));

        // Empty settings
        var emptySettings = new TendrilSettings();
        Assert.False(VaultSetupView.HasConfiguredVaults(emptySettings));

        // Disabled vault in Vaults
        var disabledVaultsSettings = new TendrilSettings
        {
            Vaults = new List<VaultSettings>
            {
                new()
                {
                    Id = "vault-disabled",
                    Enabled = false,
                    RepoUrl = "https://github.com/org/vault.git"
                }
            }
        };
        Assert.False(VaultSetupView.HasConfiguredVaults(disabledVaultsSettings));

        // Vault with empty or whitespace RepoUrl
        var emptyRepoUrlSettings = new TendrilSettings
        {
            Vaults = new List<VaultSettings>
            {
                new()
                {
                    Id = "vault-empty-url",
                    Enabled = true,
                    RepoUrl = "   "
                }
            }
        };
        Assert.False(VaultSetupView.HasConfiguredVaults(emptyRepoUrlSettings));

        // Disabled singular Vault
        var disabledSingleVaultSettings = new TendrilSettings
        {
            Vault = new VaultSettings
            {
                Id = "vault-single-disabled",
                Enabled = false,
                RepoUrl = "https://github.com/org/vault.git"
            }
        };
        Assert.False(VaultSetupView.HasConfiguredVaults(disabledSingleVaultSettings));
    }

    [Fact]
    public void GetInitialVaultStatuses_PopulatesConfiguredStatuses()
    {
        var lastSynced = DateTimeOffset.UtcNow.AddHours(-2);
        var settings = new TendrilSettings
        {
            Vaults = new List<VaultSettings>
            {
                new()
                {
                    Id = "v1",
                    Name = "Vault One",
                    Enabled = true,
                    RepoUrl = "https://github.com/org/vault-one.git",
                    LocalPath = "/path/to/v1",
                    AlwaysUpToDate = true,
                    LastSyncedAt = lastSynced
                },
                new()
                {
                    Id = "v2-disabled",
                    Name = "Disabled Vault",
                    Enabled = false,
                    RepoUrl = "https://github.com/org/vault-two.git"
                },
                new()
                {
                    Id = "v3",
                    Name = "",
                    Enabled = true,
                    RepoUrl = "https://github.com/org/custom-repo-name.git",
                    LocalPath = "/path/to/v3",
                    AlwaysUpToDate = false,
                    LastSyncedAt = null
                }
            }
        };

        var statuses = VaultSetupView.GetInitialVaultStatuses(settings);

        Assert.NotNull(statuses);
        Assert.Equal(2, statuses.Count);

        var first = statuses[0];
        Assert.Equal("v1", first.Id);
        Assert.Equal("Vault One", first.Name);
        Assert.True(first.IsConfigured);
        Assert.Equal("https://github.com/org/vault-one.git", first.RepoUrl);
        Assert.Equal("/path/to/v1", first.LocalPath);
        Assert.True(first.AlwaysUpToDate);
        Assert.Equal(lastSynced, first.LastSyncedAt);

        var second = statuses[1];
        Assert.Equal("v3", second.Id);
        Assert.Equal("org/custom-repo-name", second.Name);
        Assert.True(second.IsConfigured);
        Assert.Equal("https://github.com/org/custom-repo-name.git", second.RepoUrl);
        Assert.Equal("/path/to/v3", second.LocalPath);
        Assert.False(second.AlwaysUpToDate);
        Assert.Null(second.LastSyncedAt);
    }
}
