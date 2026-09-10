using Ivy.Tendril.Themes;

namespace Ivy.Tendril.Test.Themes;

public class ThemeRegistryTests
{
    [Fact]
    public void Registry_ContainsAtLeastTwelveThemes()
    {
        Assert.True(TendrilThemes.All.Count >= 12, $"Expected at least 12 themes, found {TendrilThemes.All.Count}");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("cupcake")]
    [InlineData("cyberpunk")]
    [InlineData("synthwave")]
    [InlineData("retro")]
    [InlineData("dracula")]
    [InlineData("nord")]
    [InlineData("forest")]
    [InlineData("aqua")]
    [InlineData("valentine")]
    [InlineData("sunset")]
    [InlineData("coffee")]
    [InlineData("dim")]
    [InlineData("luxury")]
    [InlineData("lovably")]
    [InlineData("hellokitty")]
    public void CoreThemes_AreRegistered(string themeId)
    {
        var theme = TendrilThemes.GetTheme(themeId);
        Assert.NotNull(theme);
        Assert.Equal(themeId, theme.Id, ignoreCase: true);
    }

    [Fact]
    public void AllThemes_HaveValidProperties()
    {
        foreach (var theme in TendrilThemes.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Id), "Theme Id cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(theme.Name), $"Theme {theme.Id} Name cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(theme.Description), $"Theme {theme.Id} Description cannot be empty");
            Assert.NotNull(theme.PreviewColors);
            Assert.True(theme.PreviewColors.Length >= 4, $"Theme {theme.Id} should have at least 4 preview colors");
            Assert.All(theme.PreviewColors, c => Assert.StartsWith("#", c));
            Assert.NotNull(theme.IvyTheme);
            Assert.NotNull(theme.IvyTheme.Colors);
            Assert.NotNull(theme.IvyTheme.Colors.Light);
            Assert.NotNull(theme.IvyTheme.Colors.Dark);
        }
    }

    [Fact]
    public void GetTheme_CaseInsensitive_ReturnsMatchingTheme()
    {
        var themeLower = TendrilThemes.GetTheme("cupcake");
        var themeUpper = TendrilThemes.GetTheme("CUPCAKE");
        var themeMixed = TendrilThemes.GetTheme("CupCake");

        Assert.Same(themeLower, themeUpper);
        Assert.Same(themeLower, themeMixed);
        Assert.Equal("cupcake", themeLower.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown_theme_123")]
    public void GetTheme_UnknownOrEmpty_ReturnsDefault(string? themeId)
    {
        var theme = TendrilThemes.GetTheme(themeId);
        Assert.NotNull(theme);
        Assert.Equal(TendrilThemes.Default.Id, theme.Id);
    }

    [Fact]
    public void DraculaDark_AccentForeground_IsReadableAndDistinctFromBackground()
    {
        var dracula = TendrilThemes.GetTheme("dracula");
        Assert.NotNull(dracula);

        var darkColors = dracula.IvyTheme.Colors.Dark;
        Assert.NotNull(darkColors);

        Assert.Equal("#44475a", darkColors.Accent);
        Assert.Equal("#f8f8f2", darkColors.AccentForeground);
        Assert.NotEqual(darkColors.Background, darkColors.AccentForeground);
        Assert.Equal("#282a36", darkColors.Background);
    }

    [Fact]
    public void DraculaTheme_DarkColors_HaveProperAccentContrast()
    {
        var dracula = TendrilThemes.GetTheme("dracula");
        Assert.NotNull(dracula);
        Assert.NotNull(dracula.IvyTheme?.Colors?.Dark);
        var dark = dracula.IvyTheme.Colors.Dark;
        Assert.Equal("#44475a", dark.Accent, ignoreCase: true);
        Assert.Equal("#f8f8f2", dark.AccentForeground, ignoreCase: true);
        Assert.NotEqual(dark.Background, dark.AccentForeground, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(dark.Accent, dark.AccentForeground, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForestTheme_DarkColors_HaveProperAccentContrast()
    {
        var forest = TendrilThemes.GetTheme("forest");
        Assert.NotNull(forest);
        Assert.NotNull(forest.IvyTheme?.Colors?.Dark);
        var dark = forest.IvyTheme.Colors.Dark;
        Assert.Equal("#243328", dark.Accent, ignoreCase: true);
        Assert.Equal("#ebfaef", dark.AccentForeground, ignoreCase: true);
        Assert.NotEqual(dark.Background, dark.AccentForeground, StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(dark.Accent, dark.AccentForeground, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LovablyTheme_HasValidContrastAndTokens()
    {
        var lovably = TendrilThemes.GetTheme("lovably");
        Assert.NotNull(lovably);
        Assert.Equal("Lovably", lovably.Name);
        Assert.True(lovably.IsDark);
        Assert.Equal(["#ff2e7e", "#8b5cf6", "#ff7a45", "#09090b"], lovably.PreviewColors);

        var light = lovably.IvyTheme?.Colors?.Light;
        Assert.NotNull(light);
        Assert.Equal("#ff2e7e", light.Primary, ignoreCase: true);
        Assert.Equal("#ffffff", light.PrimaryForeground, ignoreCase: true);
        Assert.Equal("#7c3aed", light.Secondary, ignoreCase: true);
        Assert.Equal("#ff7a45", light.Accent, ignoreCase: true);
        Assert.Equal("#faf8f5", light.Background, ignoreCase: true);
        Assert.Equal("#18181b", light.Foreground, ignoreCase: true);
        Assert.NotEqual(light.Background, light.Foreground, StringComparer.OrdinalIgnoreCase);

        var dark = lovably.IvyTheme?.Colors?.Dark;
        Assert.NotNull(dark);
        Assert.Equal("#ff2e7e", dark.Primary, ignoreCase: true);
        Assert.Equal("#ffffff", dark.PrimaryForeground, ignoreCase: true);
        Assert.Equal("#8b5cf6", dark.Secondary, ignoreCase: true);
        Assert.Equal("#ff7a45", dark.Accent, ignoreCase: true);
        Assert.Equal("#09090b", dark.Background, ignoreCase: true);
        Assert.Equal("#f4f4f5", dark.Foreground, ignoreCase: true);
        Assert.NotEqual(dark.Background, dark.Foreground, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void HelloKittyTheme_HasValidContrastAndTokens()
    {
        var helloKitty = TendrilThemes.GetTheme("hellokitty");
        Assert.NotNull(helloKitty);
        Assert.Equal("Hello Kitty", helloKitty.Name);
        Assert.False(helloKitty.IsDark);
        Assert.Equal(["#ff7da7", "#ff3366", "#ffd166", "#fff5f8"], helloKitty.PreviewColors);

        var light = helloKitty.IvyTheme?.Colors?.Light;
        Assert.NotNull(light);
        Assert.Equal("#ff7da7", light.Primary, ignoreCase: true);
        Assert.Equal("#ffffff", light.PrimaryForeground, ignoreCase: true);
        Assert.Equal("#ffb3c6", light.Secondary, ignoreCase: true);
        Assert.Equal("#ff3366", light.Accent, ignoreCase: true);
        Assert.Equal("#fff5f8", light.Background, ignoreCase: true);
        Assert.Equal("#2d1520", light.Foreground, ignoreCase: true);
        Assert.NotEqual(light.Background, light.Foreground, StringComparer.OrdinalIgnoreCase);

        var dark = helloKitty.IvyTheme?.Colors?.Dark;
        Assert.NotNull(dark);
        Assert.Equal("#ff6599", dark.Primary, ignoreCase: true);
        Assert.Equal("#1f0e16", dark.PrimaryForeground, ignoreCase: true);
        Assert.Equal("#ff9ebb", dark.Secondary, ignoreCase: true);
        Assert.Equal("#ff3366", dark.Accent, ignoreCase: true);
        Assert.Equal("#1f141a", dark.Background, ignoreCase: true);
        Assert.Equal("#fff0f5", dark.Foreground, ignoreCase: true);
        Assert.NotEqual(dark.Background, dark.Foreground, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllThemes_DestructiveColors_MeetWcagContrastRatio()
    {
        foreach (var theme in TendrilThemes.All)
        {
            var light = theme.IvyTheme?.Colors?.Light;
            Assert.NotNull(light);
            Assert.False(string.IsNullOrWhiteSpace(light.Destructive), $"Theme {theme.Id} Light Destructive cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(light.DestructiveForeground), $"Theme {theme.Id} Light DestructiveForeground cannot be empty");

            var lightRatio = CalculateContrastRatio(light.Destructive, light.DestructiveForeground);
            Assert.True(lightRatio >= 4.5,
                $"Theme '{theme.Id}' Light mode destructive contrast ratio is {lightRatio:F2}:1 ({light.Destructive} vs {light.DestructiveForeground}), expected at least 4.5:1 (WCAG AA).");

            var dark = theme.IvyTheme?.Colors?.Dark;
            Assert.NotNull(dark);
            Assert.False(string.IsNullOrWhiteSpace(dark.Destructive), $"Theme {theme.Id} Dark Destructive cannot be empty");
            Assert.False(string.IsNullOrWhiteSpace(dark.DestructiveForeground), $"Theme {theme.Id} Dark DestructiveForeground cannot be empty");

            var darkRatio = CalculateContrastRatio(dark.Destructive, dark.DestructiveForeground);
            Assert.True(darkRatio >= 4.5,
                $"Theme '{theme.Id}' Dark mode destructive contrast ratio is {darkRatio:F2}:1 ({dark.Destructive} vs {dark.DestructiveForeground}), expected at least 4.5:1 (WCAG AA).");
        }
    }

    [Fact]
    public void DefaultTheme_DestructiveForeground_IsAccessible()
    {
        var light = TendrilThemes.Default.IvyTheme.Colors!.Light!;
        var dark = TendrilThemes.Default.IvyTheme.Colors!.Dark!;

        Assert.Equal("#000000", light.DestructiveForeground, ignoreCase: true);
        Assert.Equal("#000000", dark.DestructiveForeground, ignoreCase: true);

        Assert.True(CalculateContrastRatio(light.Destructive!, light.DestructiveForeground!) >= 4.5,
            "Default theme Light mode destructive contrast ratio is below the WCAG AA minimum of 4.5:1.");
        Assert.True(CalculateContrastRatio(dark.Destructive!, dark.DestructiveForeground!) >= 4.5,
            "Default theme Dark mode destructive contrast ratio is below the WCAG AA minimum of 4.5:1.");
    }

    [Fact]
    public void CreateDefaultIvyTheme_DoesNotMutateIvyDefault()
    {
        TendrilThemes.CreateDefaultIvyTheme();

        Assert.Equal("#ffffff", Theme.Default.Colors!.Light!.DestructiveForeground, ignoreCase: true);
        Assert.Equal("#ffffff", Theme.Default.Colors!.Dark!.DestructiveForeground, ignoreCase: true);
    }

    private static double CalculateContrastRatio(string hex1, string hex2)
    {
        var l1 = CalculateRelativeLuminance(hex1);
        var l2 = CalculateRelativeLuminance(hex2);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static double CalculateRelativeLuminance(string hex)
    {
        var (r, g, b) = ParseHexColor(hex);
        var rLinear = ToLinear(r / 255.0);
        var gLinear = ToLinear(g / 255.0);
        var bLinear = ToLinear(b / 255.0);

        return 0.2126 * rLinear + 0.7152 * gLinear + 0.0722 * bLinear;
    }

    private static double ToLinear(double channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        var cleanHex = hex.Trim().TrimStart('#');
        if (cleanHex.Length == 3)
        {
            cleanHex = string.Concat(cleanHex[0], cleanHex[0], cleanHex[1], cleanHex[1], cleanHex[2], cleanHex[2]);
        }

        if (cleanHex.Length >= 6 &&
            byte.TryParse(cleanHex[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(cleanHex[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(cleanHex[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return (r, g, b);
        }

        throw new ArgumentException($"Invalid hex color: {hex}", nameof(hex));
    }
}


