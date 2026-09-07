using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Ivy.Tendril.Themes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class VaultThemeTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-vault-theme-test");
    private readonly string _originalTendrilHome;

    public VaultThemeTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
settings:
  beta: false
projects: []
verifications: []
";
        File.WriteAllText(Path.Combine(_tempDir.Path, "config.yaml"), yaml);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
        _tempDir.Dispose();
        TendrilThemes.ClearVaultThemes();
    }

    private ConfigService CreateConfig() => new(NullLogger<ConfigService>.Instance);

    [Fact]
    public void VaultThemeManifest_SerializationRoundtrip_PreservesAllFields()
    {
        var manifest = new VaultThemeManifest
        {
            Id = "vault-cyberpunk-neon",
            Name = "Cyberpunk Neon",
            Description = "A futuristic vibrant theme for team vault",
            IsDark = true,
            PreviewColors = new[] { "#ff007f", "#00f0ff", "#ffe600", "#0a0a14" },
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "developer@acme.corp",
            IvyTheme = new Ivy.Theme
            {
                Colors = new Ivy.ThemeColorScheme
                {
                    Dark = new Ivy.ThemeColors
                    {
                        Primary = "#ff007f",
                        PrimaryForeground = "#ffffff",
                        Secondary = "#00f0ff",
                        Accent = "#ffe600",
                        Background = "#0a0a14",
                        Foreground = "#f0f0f5",
                        Card = "#121224",
                        CardForeground = "#f0f0f5"
                    },
                    Light = new Ivy.ThemeColors
                    {
                        Primary = "#d6006b",
                        PrimaryForeground = "#ffffff",
                        Secondary = "#00b4d8",
                        Accent = "#d4a373",
                        Background = "#ffffff",
                        Foreground = "#111827",
                        Card = "#f8fafc",
                        CardForeground = "#111827"
                    }
                },
                FontFamily = "JetBrains Mono",
                BorderRadiusBoxes = "0.75rem"
            }
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        var deserialized = JsonSerializer.Deserialize<VaultThemeManifest>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(deserialized);
        Assert.Equal("vault-cyberpunk-neon", deserialized.Id);
        Assert.Equal("Cyberpunk Neon", deserialized.Name);
        Assert.Equal("A futuristic vibrant theme for team vault", deserialized.Description);
        Assert.True(deserialized.IsDark);
        Assert.Equal("developer@acme.corp", deserialized.UpdatedBy);
        Assert.Equal(4, deserialized.PreviewColors.Length);
        Assert.NotNull(deserialized.IvyTheme?.Colors?.Dark);
        Assert.Equal("#ff007f", deserialized.IvyTheme.Colors.Dark.Primary);
        Assert.Equal("0.75rem", deserialized.IvyTheme.BorderRadiusBoxes);
        Assert.Equal("JetBrains Mono", deserialized.IvyTheme.FontFamily);
    }

    [Fact]
    public void TendrilThemes_RegisterVaultTheme_DynamicallyExposesAndResolvesTheme()
    {
        var manifest = new VaultThemeManifest
        {
            Id = "vault-emerald-glow",
            Name = "Emerald Glow",
            Description = "Shared emerald theme",
            IsDark = true,
            PreviewColors = new[] { "#10b981", "#34d399", "#064e3b", "#022c22" },
            IvyTheme = new Ivy.Theme
            {
                Colors = new Ivy.ThemeColorScheme
                {
                    Dark = new Ivy.ThemeColors
                    {
                        Primary = "#10b981",
                        PrimaryForeground = "#ffffff",
                        Background = "#022c22",
                        Foreground = "#ecfdf5"
                    }
                }
            }
        };

        try
        {
            TendrilThemes.RegisterVaultTheme(manifest, "vault-alpha", "Alpha Team");

            // Theme should be visible in All
            var all = TendrilThemes.All;
            Assert.Contains(all, t => t.Id == "vault-emerald-glow");

            // Look up by ID
            var theme = TendrilThemes.GetTheme("vault-emerald-glow");
            Assert.NotNull(theme);
            Assert.Equal("Emerald Glow", theme.Name);
            Assert.True(theme.IsVaultTheme);
            Assert.Equal("vault-alpha", theme.VaultId);
            Assert.Equal("Alpha Team", theme.VaultName);

            // Look up case-insensitively
            var themeUpper = TendrilThemes.GetTheme("VAULT-EMERALD-GLOW");
            Assert.Same(theme, themeUpper);

            // Remove vault theme
            TendrilThemes.RemoveVaultTheme("vault-emerald-glow");
            var themeAfterRemoval = TendrilThemes.GetTheme("vault-emerald-glow");
            Assert.Equal(TendrilThemes.Default.Id, themeAfterRemoval.Id);
        }
        finally
        {
            TendrilThemes.RemoveVaultTheme("vault-emerald-glow");
        }
    }

    [Fact]
    public void TendrilThemes_ExtractPreviewColors_ReturnsFourDistinctColors()
    {
        var theme = new Ivy.Theme
        {
            Colors = new Ivy.ThemeColorScheme
            {
                Dark = new Ivy.ThemeColors
                {
                    Primary = "#6366f1",
                    Secondary = "#8b5cf6",
                    Accent = "#ec4899",
                    Background = "#0f172a"
                }
            }
        };

        var colors = TendrilThemes.ExtractPreviewColors(theme, isDark: true);
        Assert.Equal(4, colors.Length);
        Assert.Equal("#6366f1", colors[0]);
        Assert.Equal("#8b5cf6", colors[1]);
        Assert.Equal("#ec4899", colors[2]);
        Assert.Equal("#0f172a", colors[3]);
    }

    [Fact]
    public async Task VaultService_SaveGetDeleteTheme_WorksEndToEnd()
    {
        var config = CreateConfig();
        var vaultService = new VaultService(config, NullLogger<VaultService>.Instance);

        // Setup a local vault folder structure in temp dir
        var vaultRepoDir = Path.Combine(_tempDir.Path, "vaults", "team-vault-1");
        Directory.CreateDirectory(vaultRepoDir);

        // Add vault to config
        config.MutateAndSave(s =>
        {
            s.Vaults = new()
            {
                new VaultSettings
                {
                    Id = "v1",
                    Name = "Engineering Vault",
                    RepoUrl = "https://github.com/example/vault.git",
                    LocalPath = vaultRepoDir
                }
            };
        });

        // Initialize git repo in the vault directory so git commit/stage can function or fallback cleanly
        var gitInit = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = "init",
            WorkingDirectory = vaultRepoDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        await gitInit!.WaitForExitAsync();

        var manifest = new VaultThemeManifest
        {
            Id = "team-corporate-blue",
            Name = "Corporate Blue",
            Description = "Standard blue team palette",
            IsDark = false,
            PreviewColors = new[] { "#2563eb", "#3b82f6", "#60a5fa", "#ffffff" },
            UpdatedBy = "alice@acme.corp",
            IvyTheme = new Ivy.Theme
            {
                Colors = new Ivy.ThemeColorScheme
                {
                    Light = new Ivy.ThemeColors
                    {
                        Primary = "#2563eb",
                        Background = "#ffffff"
                    }
                }
            }
        };

        // 1. Save theme to vault
        var saveResult = await vaultService.SaveThemeToVaultAsync(manifest, "v1");
        Assert.True(saveResult.Success, saveResult.ErrorMessage);

        // 2. File exists on disk in themes/
        var themeFilePath = Path.Combine(vaultRepoDir, "themes", "team-corporate-blue.json");
        Assert.True(File.Exists(themeFilePath), $"Expected file at {themeFilePath}");

        // 3. Theme is registered in TendrilThemes
        var registeredTheme = TendrilThemes.GetTheme("team-corporate-blue");
        Assert.Equal("Corporate Blue", registeredTheme.Name);
        Assert.True(registeredTheme.IsVaultTheme);
        Assert.Equal("v1", registeredTheme.VaultId);
        Assert.Equal("Engineering Vault", registeredTheme.VaultName);

        // 4. GetThemesAsync retrieves it
        var themes = await vaultService.GetThemesAsync("v1");
        Assert.Single(themes);
        Assert.Equal("team-corporate-blue", themes[0].Id);
        Assert.Equal("Corporate Blue", themes[0].Name);
        Assert.Equal("alice@acme.corp", themes[0].UpdatedBy);

        // 5. Catalog contains the theme
        var catalog = await vaultService.GetCatalogAsync("v1");
        Assert.NotNull(catalog);
        Assert.Single(catalog.Themes);
        Assert.Equal("team-corporate-blue", catalog.Themes[0].Id);

        // 6. Delete theme
        var deleteResult = await vaultService.DeleteThemeFromVaultAsync("team-corporate-blue", "v1");
        Assert.True(deleteResult.Success, deleteResult.ErrorMessage);
        Assert.False(File.Exists(themeFilePath));

        // 7. Theme no longer returned by GetThemesAsync
        var remainingThemes = await vaultService.GetThemesAsync("v1");
        Assert.Empty(remainingThemes);

        // 8. Theme removed from registry
        var unregisteredTheme = TendrilThemes.GetTheme("team-corporate-blue");
        Assert.Equal(TendrilThemes.Default.Id, unregisteredTheme.Id);
    }

    [Fact]
    public void BetaGating_SharedOptions_OnlyActiveWhenBetaEnabled()
    {
        var origTendril = Environment.GetEnvironmentVariable("TENDRIL_BETA");
        var origIvy = Environment.GetEnvironmentVariable("IVY_BETA");

        try
        {
            Environment.SetEnvironmentVariable("TENDRIL_BETA", null);
            Environment.SetEnvironmentVariable("IVY_BETA", null);

            var config = CreateConfig();

            // Default config: beta is false
            Assert.False(BetaHelper.IsBeta(null, config));

            // Enable beta
            config.MutateAndSave(s => s.Beta = true);
            Assert.True(BetaHelper.IsBeta(null, config));

            // Disable beta
            config.MutateAndSave(s => s.Beta = false);
            Assert.False(BetaHelper.IsBeta(null, config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_BETA", origTendril);
            Environment.SetEnvironmentVariable("IVY_BETA", origIvy);
        }
    }
}
