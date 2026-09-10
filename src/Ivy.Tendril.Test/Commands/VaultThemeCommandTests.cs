using System.Text.Json;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Infrastructure;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Ivy.Tendril.Themes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Ivy.Tendril.Test.Commands;

public class VaultThemeSettingsValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VaultThemeGetSettings_RejectsEmptyThemeId(string id)
    {
        var settings = new VaultThemeGetSettings { ThemeId = id };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("theme-id", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultThemeAddSettings_RequiresSource()
    {
        var settings = new VaultThemeAddSettings();
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Provide theme JSON", result.Message);
    }

    [Fact]
    public void VaultThemeAddSettings_RejectsMultipleSources()
    {
        var settings = new VaultThemeAddSettings { FilePath = "theme.json", Stdin = true };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("exactly one way", result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VaultThemeCreateSettings_RejectsEmptyId(string id)
    {
        var settings = new VaultThemeCreateSettings { Id = id, Name = "My Theme" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("id", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsBuiltInCollisionWithoutForce()
    {
        var settings = new VaultThemeCreateSettings { Id = "dracula", Name = "Dracula Clone", Force = false };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("collides with a built-in theme", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_AllowsBuiltInCollisionWithForce()
    {
        var settings = new VaultThemeCreateSettings { Id = "dracula", Name = "Dracula Clone", Force = true };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VaultThemeCreateSettings_RejectsEmptyName(string name)
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = name };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("name", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsUnknownBasePreset()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", BasePreset = "non-existent" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Unknown base preset", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsBothDarkAndLight()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", Dark = true, Light = true };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("both --dark and --light", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsInvalidHexPrimary()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", Primary = "blue" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Invalid hex color", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsInvalidColorOverrideFormat()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", Colors = ["primary"] };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Expected '<token>=<hex>'", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsUnknownColorToken()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", Colors = ["fakeToken=#ff0000"] };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Unknown color token", result.Message);
    }

    [Fact]
    public void VaultThemeCreateSettings_RejectsInvalidHexInColorOverride()
    {
        var settings = new VaultThemeCreateSettings { Id = "my-theme", Name = "My Theme", Colors = ["primary=notAColor"] };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Invalid hex color", result.Message);
    }

    [Fact]
    public void VaultThemeSetSettings_RejectsUnknownField()
    {
        var settings = new VaultThemeSetSettings { Id = "my-theme", Field = "unknownField", Value = "val" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Unknown field", result.Message);
        Assert.Contains("Valid fields:", result.Message);
    }

    [Fact]
    public void VaultThemeSetSettings_RejectsNonHexColor()
    {
        var settings = new VaultThemeSetSettings { Id = "my-theme", Field = "primary", Value = "red" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Invalid hex color", result.Message);
    }

    [Fact]
    public void VaultThemeSetSettings_RejectsInvalidPreview()
    {
        var settings = new VaultThemeSetSettings { Id = "my-theme", Field = "preview", Value = "#111,#222,#333" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("Invalid preview colors", result.Message);
    }

    [Fact]
    public void VaultThemeSetSettings_AcceptsValidColorToken()
    {
        var settings = new VaultThemeSetSettings { Id = "my-theme", Field = "light.primary", Value = "#ff0000" };
        var result = settings.Validate();
        Assert.True(result.Successful);
    }

    [Fact]
    public void VaultThemeDeleteSettings_RejectsEmptyThemeId()
    {
        var settings = new VaultThemeDeleteSettings { ThemeId = "" };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    [Fact]
    public void VaultThemeApplySettings_RejectsEmptyThemeId()
    {
        var settings = new VaultThemeApplySettings { ThemeId = "" };
        var result = settings.Validate();
        Assert.False(result.Successful);
    }
}

[Collection("TendrilHome")]
public class VaultThemeCommandsExecutionTests : IDisposable
{
    public void Dispose()
    {
        CliOutput.PlainOverride = null;
    }

    private static string CaptureCommandOutput(Action<CommandApp, FakeVaultService> action)
    {
        lock (TestLocks.ConsoleLock)
        {
            var writer = new StringWriter();
            var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer)
            });
            ansiConsole.Profile.Width = 240;

            var originalAnsiConsole = AnsiConsole.Console;
            var originalOut = Console.Out;
            var originalPlainOverride = CliOutput.PlainOverride;

            AnsiConsole.Console = ansiConsole;
            Console.SetOut(writer);
            CliOutput.PlainOverride = true;

            try
            {
                var (app, fakeVault) = CreateTestApp(ansiConsole);
                action(app, fakeVault);
            }
            finally
            {
                CliOutput.PlainOverride = originalPlainOverride;
                Console.SetOut(originalOut);
                AnsiConsole.Console = originalAnsiConsole;
            }

            return writer.ToString();
        }
    }

    private static (CommandApp App, FakeVaultService VaultService) CreateTestApp(IAnsiConsole console, ConfigService? configService = null)
    {
        var fakeVault = new FakeVaultService();
        var services = new ServiceCollection();
        services.AddSingleton<IVaultService>(fakeVault);
        configService ??= new ConfigService(NullLogger<ConfigService>.Instance);
        services.AddSingleton<IConfigService>(configService);
        services.AddSingleton<ConfigService>(configService);

        var app = Program.ConfigureCliCommands(services, console);
        return (app, fakeVault);
    }

    private static VaultThemeManifest CreateSampleTheme(string id = "cyber-neon", string name = "Cyber Neon", bool isDark = true)
    {
        return new VaultThemeManifest
        {
            Id = id,
            Name = name,
            Description = "A neon futuristic theme",
            IsDark = isDark,
            PreviewColors = ["#ff007f", "#00f0ff", "#ffe600", "#0a0a14"],
            UpdatedAt = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            UpdatedBy = "tester@example.com",
            IvyTheme = new Ivy.Theme
            {
                Name = name,
                FontFamily = "Fira Code",
                FontSize = "15px",
                BorderRadiusBoxes = "10px",
                BorderRadiusFields = "8px",
                BorderRadiusSelectors = "6px",
                ShadowBoxes = true,
                ShadowFields = false,
                ShadowSelectors = true,
                Colors = new Ivy.ThemeColorScheme
                {
                    Light = new Ivy.ThemeColors
                    {
                        Primary = "#0055ff",
                        Secondary = "#64748b",
                        Accent = "#f59e0b",
                        Background = "#ffffff",
                        Foreground = "#0f172a"
                    },
                    Dark = new Ivy.ThemeColors
                    {
                        Primary = "#ff007f",
                        Secondary = "#00f0ff",
                        Accent = "#ffe600",
                        Background = "#0a0a14",
                        Foreground = "#f8fafc"
                    }
                }
            }
        };
    }

    [Fact]
    public void VaultThemeList_WithoutJson_RendersTableWithModeAndId()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme()];
            exit = app.Run(["vault", "theme", "list"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("cyber-neon", output);
        Assert.Contains("Cyber Neon", output);
        Assert.Contains("Dark", output);
    }

    [Fact]
    public void VaultThemeList_WithJson_OutputsJsonWithId()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme()];
            exit = app.Run(["vault", "theme", "list", "--json"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("cyber-neon", output);
        Assert.Contains("\"id\": \"cyber-neon\"", output);
    }

    [Fact]
    public void VaultThemeList_Empty_PrintsNoThemesFound()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [];
            exit = app.Run(["vault", "theme", "list"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("No themes found", output);
    }

    [Fact]
    public void VaultThemeGet_WithoutJson_RendersDetailsAndColorTable()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme()];
            exit = app.Run(["vault", "theme", "get", "cyber-neon"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("Cyber Neon", output);
        Assert.Contains("#ff007f", output);
        Assert.Contains("#0055ff", output);
    }

    [Fact]
    public void VaultThemeGet_WithJson_EmitsCamelCaseIvyTheme()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme()];
            exit = app.Run(["vault", "theme", "get", "cyber-neon", "--json"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("\"ivyTheme\":", output);
        Assert.Contains("\"previewColors\":", output);
    }

    [Fact]
    public void VaultThemeGet_UnknownId_Exits1AndListsAvailable()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme(id: "alpha-theme")];
            exit = app.Run(["vault", "theme", "get", "unknown-theme"]);
        });

        Assert.Equal(1, exit);
        Assert.Contains("not found in vault", output);
        Assert.Contains("alpha-theme", output);
    }

    [Fact]
    public void VaultThemeAdd_WithManifestFile_SavesThemeToVault()
    {
        using var tempDir = new TempDirectoryFixture("vault-theme-add-test");
        var filePath = Path.Combine(tempDir.Path, "manifest.json");
        var manifest = CreateSampleTheme(id: "my-imported-theme", name: "Imported Theme");
        File.WriteAllText(filePath, JsonSerializer.Serialize(manifest, VaultThemeCommandHelpers.ManifestJsonOptions));

        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            exit = app.Run(["vault", "theme", "add", "--file", filePath]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("added to vault successfully", output);
    }

    [Fact]
    public void VaultThemeAdd_ThemeShapedJsonFallback_ImportsSuccessfully()
    {
        using var tempDir = new TempDirectoryFixture("vault-theme-add-fallback-test");
        var filePath = Path.Combine(tempDir.Path, "theme.json");
        var theme = new Ivy.Theme
        {
            Name = "Standalone Theme",
            Colors = new Ivy.ThemeColorScheme
            {
                Light = new Ivy.ThemeColors { Primary = "#123456", Background = "#ffffff" }
            }
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(theme, VaultThemeCommandHelpers.ManifestJsonOptions));

        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            exit = app.Run(["vault", "theme", "add", "--file", filePath, "--id", "custom-id"]);
        });

        Assert.Equal(0, exit);
        Assert.Contains("custom-id", output);
    }

    [Fact]
    public void VaultThemeAdd_BlankName_Exits1WithoutCallingService()
    {
        using var tempDir = new TempDirectoryFixture("vault-theme-add-blank-test");
        var filePath = Path.Combine(tempDir.Path, "manifest.json");
        var manifest = new VaultThemeManifest
        {
            Id = "blank-name-theme",
            Name = "",
            IvyTheme = new Ivy.Theme
            {
                Colors = new Ivy.ThemeColorScheme
                {
                    Light = new Ivy.ThemeColors { Primary = "#123456" }
                }
            }
        };
        File.WriteAllText(filePath, JsonSerializer.Serialize(manifest, VaultThemeCommandHelpers.ManifestJsonOptions));

        var exit = -1;
        FakeVaultService? capturedVault = null;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            capturedVault = fakeVault;
            exit = app.Run(["vault", "theme", "add", "--file", filePath]);
        });

        Assert.Equal(1, exit);
        Assert.Contains("Theme name cannot be empty", output);
        Assert.Null(capturedVault?.LastSavedTheme);
    }

    [Fact]
    public void VaultThemeAdd_ServiceFailure_Exits1WithError()
    {
        using var tempDir = new TempDirectoryFixture("vault-theme-add-fail-test");
        var filePath = Path.Combine(tempDir.Path, "manifest.json");
        var manifest = CreateSampleTheme();
        File.WriteAllText(filePath, JsonSerializer.Serialize(manifest, VaultThemeCommandHelpers.ManifestJsonOptions));

        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.SaveThemeResultToReturn = new VaultResult(false, "Vault permission denied.");
            exit = app.Run(["vault", "theme", "add", "--file", filePath]);
        });

        Assert.Equal(1, exit);
        Assert.Contains("Error:", output);
        Assert.Contains("Vault permission denied.", output);
    }

    [Fact]
    public void VaultThemeCreate_FromPreset_ProducesExpectedManifestAndPreview()
    {
        var exit = -1;
        FakeVaultService? capturedVault = null;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            capturedVault = fakeVault;
            exit = app.Run([
                "vault", "theme", "create", "custom-dracula",
                "--name", "Custom Dracula",
                "--base", "dracula",
                "--dark",
                "--primary", "#ff0000",
                "--color", "ring=#00ff00"
            ]);
        });

        Assert.Equal(0, exit);
        Assert.NotNull(capturedVault?.LastSavedTheme);
        var saved = capturedVault.LastSavedTheme!;
        Assert.Equal("custom-dracula", saved.Id);
        Assert.Equal("Custom Dracula", saved.Name);
        Assert.True(saved.IsDark);
        Assert.Equal("#ff0000", saved.IvyTheme.Colors.Dark.Primary);
        Assert.Equal("#00ff00", saved.IvyTheme.Colors.Dark.Ring);
        Assert.Equal("#ff0000", saved.PreviewColors[0]);
    }

    [Fact]
    public void VaultThemeCreate_BuiltInCollisionWithoutForce_FailsValidation()
    {
        var settings = new VaultThemeCreateSettings
        {
            Id = "dracula",
            Name = "Colliding Dracula",
            Force = false
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("collides with a built-in theme", result.Message);

        CaptureCommandOutput((app, fakeVault) =>
        {
            var ex = Assert.Throws<CommandRuntimeException>(() =>
                app.Run(["vault", "theme", "create", "dracula", "--name", "Colliding Dracula"]));
            Assert.Contains("collides with a built-in theme", ex.Message);
        });
    }

    [Fact]
    public void VaultThemeSet_MutatesExpectedFields()
    {
        var theme = CreateSampleTheme(id: "mutating-theme");

        // 1. name
        CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [theme];
            var exit = app.Run(["vault", "theme", "set", "mutating-theme", "name", "Renamed Theme"]);
            Assert.Equal(0, exit);
            Assert.Equal("Renamed Theme", fakeVault.LastSavedTheme?.Name);
        });

        // 2. mode dark
        CaptureCommandOutput((app, fakeVault) =>
        {
            theme.IsDark = false;
            fakeVault.ThemesToReturn = [theme];
            var exit = app.Run(["vault", "theme", "set", "mutating-theme", "mode", "dark"]);
            Assert.Equal(0, exit);
            Assert.True(fakeVault.LastSavedTheme?.IsDark);
        });

        // 3. light.primary
        CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [theme];
            var exit = app.Run(["vault", "theme", "set", "mutating-theme", "light.primary", "#112233"]);
            Assert.Equal(0, exit);
            Assert.Equal("#112233", fakeVault.LastSavedTheme?.IvyTheme.Colors.Light.Primary);
        });

        // 4. preview
        CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [theme];
            var exit = app.Run(["vault", "theme", "set", "mutating-theme", "preview", "#111111,#222222,#333333,#444444"]);
            Assert.Equal(0, exit);
            Assert.Equal(new[] { "#111111", "#222222", "#333333", "#444444" }, fakeVault.LastSavedTheme?.PreviewColors ?? []);
        });
    }

    [Fact]
    public void VaultThemeSet_UnknownThemeId_Exits1()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [];
            exit = app.Run(["vault", "theme", "set", "non-existent", "name", "New Name"]);
        });

        Assert.Equal(1, exit);
        Assert.Contains("not found in vault", output);
    }

    [Fact]
    public void VaultThemeDelete_KnownId_CallsDeleteOnService()
    {
        var exit = -1;
        FakeVaultService? capturedVault = null;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [CreateSampleTheme(id: "to-delete")];
            capturedVault = fakeVault;
            exit = app.Run(["vault", "theme", "delete", "to-delete"]);
        });

        Assert.Equal(0, exit);
        Assert.Equal("to-delete", capturedVault?.LastDeletedThemeId);
        Assert.Contains("deleted from vault successfully", output);
    }

    [Fact]
    public void VaultThemeDelete_UnknownId_Exits1AndDoesNotCallService()
    {
        var exit = -1;
        FakeVaultService? capturedVault = null;
        var output = CaptureCommandOutput((app, fakeVault) =>
        {
            fakeVault.ThemesToReturn = [];
            capturedVault = fakeVault;
            exit = app.Run(["vault", "theme", "delete", "unknown-theme"]);
        });

        Assert.Equal(1, exit);
        Assert.Null(capturedVault?.LastDeletedThemeId);
        Assert.Contains("not found in vault", output);
    }
}

[Collection("TendrilHome")]
public class VaultThemeApplyCommandTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("tendril-vault-apply-test");
    private readonly string _originalTendrilHome;

    public VaultThemeApplyCommandTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME") ?? "";
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _tempDir.Path);

        var yaml = @"
settings:
  beta: false
  theme: default
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

    private static string CaptureCommandOutput(Action<CommandApp, FakeVaultService, ConfigService> action)
    {
        lock (TestLocks.ConsoleLock)
        {
            var writer = new StringWriter();
            var ansiConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(writer)
            });
            ansiConsole.Profile.Width = 240;

            var originalAnsiConsole = AnsiConsole.Console;
            var originalOut = Console.Out;
            var originalPlainOverride = CliOutput.PlainOverride;

            AnsiConsole.Console = ansiConsole;
            Console.SetOut(writer);
            CliOutput.PlainOverride = true;

            try
            {
                var fakeVault = new FakeVaultService();
                var services = new ServiceCollection();
                services.AddSingleton<IVaultService>(fakeVault);
                var configService = new ConfigService(NullLogger<ConfigService>.Instance);
                services.AddSingleton<IConfigService>(configService);
                services.AddSingleton<ConfigService>(configService);

                var app = Program.ConfigureCliCommands(services, ansiConsole);
                action(app, fakeVault, configService);
            }
            finally
            {
                CliOutput.PlainOverride = originalPlainOverride;
                Console.SetOut(originalOut);
                AnsiConsole.Console = originalAnsiConsole;
            }

            return writer.ToString();
        }
    }

    [Fact]
    public void VaultThemeApply_CallsLoadThemesAndUpdatesConfig()
    {
        var exit = -1;
        FakeVaultService? capturedVault = null;
        ConfigService? capturedConfig = null;

        var output = CaptureCommandOutput((app, fakeVault, configService) =>
        {
            capturedVault = fakeVault;
            capturedConfig = configService;

            // Register a vault theme so LoadThemes simulation has it registered
            TendrilThemes.RegisterVaultTheme(new VaultThemeManifest
            {
                Id = "custom-vault-theme",
                Name = "Custom Vault Theme",
                IvyTheme = new Ivy.Theme { Name = "Custom Vault Theme" }
            }, "vault-1", "Main Vault");

            exit = app.Run(["vault", "theme", "apply", "custom-vault-theme"]);
        });

        Assert.Equal(0, exit);
        Assert.NotNull(capturedVault);
        Assert.True(capturedVault.LoadThemesCallCount > 0);
        Assert.NotNull(capturedConfig);
        Assert.Equal("custom-vault-theme", capturedConfig.Settings.Theme);
        Assert.Contains("Active theme set to 'custom-vault-theme'", output);
    }

    [Fact]
    public void VaultThemeApply_UnknownThemeId_Exits1WithError()
    {
        var exit = -1;
        var output = CaptureCommandOutput((app, fakeVault, configService) =>
        {
            exit = app.Run(["vault", "theme", "apply", "totally-unknown-theme"]);
        });

        Assert.Equal(1, exit);
        Assert.Contains("Error:", output);
        Assert.Contains("Unknown theme 'totally-unknown-theme'", output);
    }
}
