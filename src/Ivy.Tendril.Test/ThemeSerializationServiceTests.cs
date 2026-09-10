using System;
using System.Text.Json;
using Ivy;
using Ivy.Tendril.Services.Vault;
using Ivy.Tendril.Themes;
using Xunit;

namespace Ivy.Tendril.Test;

public class ThemeSerializationServiceTests
{
    private readonly IThemeSerializationService _service = new ThemeSerializationService();

    [Fact]
    public void TryImportTheme_WithThemeJson_ImportsSuccessfully()
    {
        var json = @"{
  ""Name"": ""Emerald Glow"",
  ""FontFamily"": ""Fira Code"",
  ""FontSize"": ""15px"",
  ""BorderRadiusBoxes"": ""0.75rem"",
  ""BorderRadiusFields"": ""0.5rem"",
  ""BorderRadiusSelectors"": ""1rem"",
  ""Colors"": {
    ""Light"": {
      ""Primary"": ""#059669"",
      ""PrimaryForeground"": ""#ffffff"",
      ""Background"": ""#f0fdf4"",
      ""Foreground"": ""#064e3b""
    },
    ""Dark"": {
      ""Primary"": ""#34d399"",
      ""PrimaryForeground"": ""#064e3b"",
      ""Background"": ""#064e3b"",
      ""Foreground"": ""#ecfdf5""
    }
  }
}";

        var success = _service.TryImportTheme(json, out var theme, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("Emerald Glow", theme.Name);
        Assert.Equal("Fira Code", theme.FontFamily);
        Assert.Equal("15px", theme.FontSize);
        Assert.Equal("0.75rem", theme.BorderRadiusBoxes);
        Assert.Equal("0.5rem", theme.BorderRadiusFields);
        Assert.Equal("1rem", theme.BorderRadiusSelectors);
        Assert.Equal("#059669", theme.Colors.Light.Primary);
        Assert.Equal("#34d399", theme.Colors.Dark.Primary);
    }

    [Fact]
    public void TryImportTheme_WithVaultThemeManifestJson_ImportsSuccessfully()
    {
        var manifest = new VaultThemeManifest
        {
            Id = "vault-manifest-theme",
            Name = "Manifest Theme",
            Description = "Theme imported from VaultThemeManifest JSON",
            IsDark = true,
            IvyTheme = new Theme
            {
                Name = "Nested Theme",
                FontFamily = "Inter",
                FontSize = "14px",
                Colors = new ThemeColorScheme
                {
                    Light = new ThemeColors
                    {
                        Primary = "#112233",
                        PrimaryForeground = "#FFFFFF"
                    },
                    Dark = new ThemeColors
                    {
                        Primary = "#445566",
                        PrimaryForeground = "#000000"
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var success = _service.TryImportTheme(json, out var theme, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("Manifest Theme", theme.Name);
        Assert.Equal("Inter", theme.FontFamily);
        Assert.Equal("14px", theme.FontSize);
        Assert.Equal("#112233", theme.Colors.Light.Primary);
        Assert.Equal("#445566", theme.Colors.Dark.Primary);
    }

    [Fact]
    public void TryImportTheme_WithThemeColorSchemeJson_ImportsSuccessfully()
    {
        var scheme = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = "#10B981",
                PrimaryForeground = "#FFFFFF",
                Background = "#FFFFFF",
                Foreground = "#111827"
            },
            Dark = new ThemeColors
            {
                Primary = "#059669",
                PrimaryForeground = "#FFFFFF",
                Background = "#111827",
                Foreground = "#F9FAFB"
            }
        };

        var json = JsonSerializer.Serialize(scheme);
        var success = _service.TryImportTheme(json, out var theme, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("Imported Theme", theme.Name);
        Assert.NotNull(theme.Colors);
        Assert.Equal("#10B981", theme.Colors.Light.Primary);
        Assert.Equal("#059669", theme.Colors.Dark.Primary);
    }

    [Fact]
    public void TryImportTheme_WithCSharpConfigurationCode_ImportsSuccessfully()
    {
        var csharpCode = @"// Add this to your server configuration:
var server = new Server()
    .UseTheme(theme => {
        theme.Name = ""Nordic Frost"";
        theme.Colors = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = ""#5E81AC"",
                PrimaryForeground = ""#ECEFF4"",
                Background = ""#ECEFF4"",
                Foreground = ""#2E3440""
            },
            Dark = new ThemeColors
            {
                Primary = ""#88C0D0"",
                PrimaryForeground = ""#2E3440"",
                Background = ""#2E3440"",
                Foreground = ""#ECEFF4""
            }
        };
        theme.FontFamily = ""Inter"";
        theme.FontSize = ""14px"";
        theme.BorderRadiusBoxes = ""0.5rem"";
        theme.BorderRadiusFields = ""0.25rem"";
        theme.BorderRadiusSelectors = ""0.75rem""; 
    });";

        var success = _service.TryImportTheme(csharpCode, out var theme, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal("Nordic Frost", theme.Name);
        Assert.Equal("Inter", theme.FontFamily);
        Assert.Equal("14px", theme.FontSize);
        Assert.Equal("0.5rem", theme.BorderRadiusBoxes);
        Assert.Equal("0.25rem", theme.BorderRadiusFields);
        Assert.Equal("0.75rem", theme.BorderRadiusSelectors);
        Assert.Equal("#5E81AC", theme.Colors.Light.Primary);
        Assert.Equal("#88C0D0", theme.Colors.Dark.Primary);
        Assert.Equal("#2E3440", theme.Colors.Dark.Background);
    }

    [Fact]
    public void TryImportTheme_WithInvalidOrEmptyInput_ReturnsFalseAndErrorMessage()
    {
        var successEmpty = _service.TryImportTheme("", out _, out var errorEmpty);
        Assert.False(successEmpty);
        Assert.NotNull(errorEmpty);

        var successWhitespace = _service.TryImportTheme("   \t\r\n  ", out _, out var errorWhitespace);
        Assert.False(successWhitespace);
        Assert.NotNull(errorWhitespace);

        var successInvalid = _service.TryImportTheme("random non-theme string", out _, out var errorInvalid);
        Assert.False(successInvalid);
        Assert.NotNull(errorInvalid);
    }

    [Fact]
    public void ExportToCSharp_GeneratesValidSnippet()
    {
        var theme = new Theme
        {
            Name = "Solarized Light",
            FontFamily = "JetBrains Mono",
            FontSize = "13px",
            BorderRadiusBoxes = "8px",
            BorderRadiusFields = "4px",
            BorderRadiusSelectors = "6px",
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#B58900",
                    PrimaryForeground = "#FDF6E3",
                    Background = "#FDF6E3",
                    Foreground = "#657B83"
                },
                Dark = new ThemeColors
                {
                    Primary = "#268BD2",
                    PrimaryForeground = "#002B36",
                    Background = "#002B36",
                    Foreground = "#839496"
                }
            }
        };

        var snippet = _service.ExportToCSharp(theme);

        Assert.Contains("var server = new Server()", snippet);
        Assert.Contains("theme.Name = \"Solarized Light\";", snippet);
        Assert.Contains("theme.FontFamily = \"JetBrains Mono\";", snippet);
        Assert.Contains("theme.FontSize = \"13px\";", snippet);
        Assert.Contains("Primary = \"#B58900\"", snippet);
        Assert.Contains("Primary = \"#268BD2\"", snippet);

        // Verify round-trip parsing of generated C# snippet
        var importSuccess = _service.TryImportTheme(snippet, out var reimported, out var importError);
        Assert.True(importSuccess, importError);
        Assert.Equal(theme.Name, reimported.Name);
        Assert.Equal(theme.FontFamily, reimported.FontFamily);
        Assert.Equal(theme.FontSize, reimported.FontSize);
        Assert.Equal(theme.Colors.Light.Primary, reimported.Colors.Light.Primary);
        Assert.Equal(theme.Colors.Dark.Primary, reimported.Colors.Dark.Primary);
    }

    [Fact]
    public void ExportToJson_GeneratesValidJson()
    {
        var theme = new Theme
        {
            Name = "Cobalt",
            FontFamily = "Segoe UI",
            FontSize = "16px",
            BorderRadiusBoxes = "12px",
            BorderRadiusFields = "6px",
            BorderRadiusSelectors = "8px",
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#0047AB",
                    PrimaryForeground = "#FFFFFF"
                },
                Dark = new ThemeColors
                {
                    Primary = "#1F75FE",
                    PrimaryForeground = "#FFFFFF"
                }
            }
        };

        var jsonIndented = _service.ExportToJson(theme, indented: true);
        var jsonCompact = _service.ExportToJson(theme, indented: false);

        Assert.Contains("\n", jsonIndented);
        Assert.DoesNotContain("\n", jsonCompact);
        Assert.Contains("\"name\": \"Cobalt\"", jsonIndented);

        // Verify round-trip parsing of generated JSON
        var importSuccess = _service.TryImportTheme(jsonIndented, out var reimported, out var importError);
        Assert.True(importSuccess, importError);
        Assert.Equal(theme.Name, reimported.Name);
        Assert.Equal(theme.FontFamily, reimported.FontFamily);
        Assert.Equal(theme.FontSize, reimported.FontSize);
        Assert.Equal(theme.Colors.Light.Primary, reimported.Colors.Light.Primary);
        Assert.Equal(theme.Colors.Dark.Primary, reimported.Colors.Dark.Primary);
    }
}
