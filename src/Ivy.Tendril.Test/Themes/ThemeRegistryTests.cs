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
}
