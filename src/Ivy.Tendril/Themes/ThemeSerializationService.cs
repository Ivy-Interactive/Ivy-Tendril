using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy;
using Ivy.Tendril.Services.Vault;

namespace Ivy.Tendril.Themes;

public class ThemeSerializationService : IThemeSerializationService
{
    public static ThemeSerializationService Default { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions IndentedExportOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions CompactExportOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public bool TryImportTheme(string raw, out Theme importedTheme, out string? errorMessage)
    {
        importedTheme = TendrilThemes.CloneTheme(Theme.Default);
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            errorMessage = "Please enter or paste a theme configuration.";
            return false;
        }

        var trimmed = raw.Trim();

        // 1. Try JSON parsing
        if (trimmed.StartsWith('{') || trimmed.EndsWith('}'))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                // 1a. Try VaultThemeManifest (has IvyTheme / ivyTheme)
                if (root.TryGetProperty("ivyTheme", out _) || root.TryGetProperty("IvyTheme", out _))
                {
                    var parsedManifest = JsonSerializer.Deserialize<VaultThemeManifest>(trimmed, JsonOptions);
                    if (parsedManifest?.IvyTheme != null)
                    {
                        importedTheme = TendrilThemes.CloneTheme(parsedManifest.IvyTheme);
                        if (!string.IsNullOrWhiteSpace(parsedManifest.Name))
                            importedTheme.Name = parsedManifest.Name;
                        return true;
                    }
                }

                // 1b. Try Theme (has Colors / colors)
                if (root.TryGetProperty("colors", out _) || root.TryGetProperty("Colors", out _))
                {
                    var parsedTheme = JsonSerializer.Deserialize<Theme>(trimmed, JsonOptions);
                    if (parsedTheme?.Colors != null && (parsedTheme.Colors.Light != null || parsedTheme.Colors.Dark != null))
                    {
                        importedTheme = TendrilThemes.CloneTheme(parsedTheme);
                        return true;
                    }
                }

                // 1c. Try ThemeColorScheme (has Light / light or Dark / dark)
                if (root.TryGetProperty("light", out _) || root.TryGetProperty("Light", out _) ||
                    root.TryGetProperty("dark", out _) || root.TryGetProperty("Dark", out _))
                {
                    var parsedScheme = JsonSerializer.Deserialize<ThemeColorScheme>(trimmed, JsonOptions);
                    if (parsedScheme?.Light != null || parsedScheme?.Dark != null)
                    {
                        importedTheme = new Theme
                        {
                            Name = "Imported Theme",
                            Colors = new ThemeColorScheme
                            {
                                Light = TendrilThemes.CloneThemeColors(parsedScheme.Light ?? ThemeColors.DefaultLight),
                                Dark = TendrilThemes.CloneThemeColors(parsedScheme.Dark ?? ThemeColors.DefaultDark)
                            }
                        };
                        return true;
                    }
                }
            }
            catch
            {
                // Fall through to C# or regex parsing
            }
        }

        // 2. Try C# configuration code parsing
        try
        {
            var parsed = TendrilThemes.CloneTheme(Theme.Default);
            var foundAny = false;

            var nameMatch = Regex.Match(trimmed, @"theme\.Name\s*=\s*""([^""]+)""");
            if (nameMatch.Success)
            {
                parsed.Name = nameMatch.Groups[1].Value;
                foundAny = true;
            }

            var fontMatch = Regex.Match(trimmed, @"theme\.FontFamily\s*=\s*""([^""]+)""");
            if (fontMatch.Success)
            {
                parsed.FontFamily = fontMatch.Groups[1].Value;
                foundAny = true;
            }

            var sizeMatch = Regex.Match(trimmed, @"theme\.FontSize\s*=\s*""([^""]+)""");
            if (sizeMatch.Success)
            {
                parsed.FontSize = sizeMatch.Groups[1].Value;
                foundAny = true;
            }

            var boxesMatch = Regex.Match(trimmed, @"theme\.BorderRadiusBoxes\s*=\s*""([^""]+)""");
            if (boxesMatch.Success)
            {
                parsed.BorderRadiusBoxes = boxesMatch.Groups[1].Value;
                foundAny = true;
            }

            var fieldsMatch = Regex.Match(trimmed, @"theme\.BorderRadiusFields\s*=\s*""([^""]+)""");
            if (fieldsMatch.Success)
            {
                parsed.BorderRadiusFields = fieldsMatch.Groups[1].Value;
                foundAny = true;
            }

            var selectorsMatch = Regex.Match(trimmed, @"theme\.BorderRadiusSelectors\s*=\s*""([^""]+)""");
            if (selectorsMatch.Success)
            {
                parsed.BorderRadiusSelectors = selectorsMatch.Groups[1].Value;
                foundAny = true;
            }

            // Extract Light and Dark blocks
            var lightBlockMatch = Regex.Match(trimmed, @"Light\s*=\s*new\s*ThemeColors\s*\{(?<content>[^\}]+)\}", RegexOptions.Singleline);
            var darkBlockMatch = Regex.Match(trimmed, @"Dark\s*=\s*new\s*ThemeColors\s*\{(?<content>[^\}]+)\}", RegexOptions.Singleline);

            if (lightBlockMatch.Success)
            {
                ApplyColorsFromBlock(lightBlockMatch.Groups["content"].Value, parsed.Colors.Light);
                foundAny = true;
            }

            if (darkBlockMatch.Success)
            {
                ApplyColorsFromBlock(darkBlockMatch.Groups["content"].Value, parsed.Colors.Dark);
                foundAny = true;
            }

            // If neither Light nor Dark block explicitly matched, try extracting color assignments anywhere
            if (!lightBlockMatch.Success && !darkBlockMatch.Success)
            {
                var colorMatches = Regex.Matches(trimmed, @"(?<key>Primary|Secondary|Accent|Background|Foreground|Destructive|Success|Warning|Info|Border|Input|Ring|Muted|Card|Popover)(?<fg>Foreground)?\s*=\s*""(?<val>#[0-9a-fA-F]{3,8}|rgba?\([^)]+\)|[a-zA-Z]+)""");
                if (colorMatches.Count > 0)
                {
                    foundAny = true;
                    ApplyColorsFromBlock(trimmed, parsed.Colors.Light);
                    ApplyColorsFromBlock(trimmed, parsed.Colors.Dark);
                }
            }

            if (foundAny)
            {
                importedTheme = parsed;
                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = $"Import parsing error: {ex.Message}";
            return false;
        }

        errorMessage = "Could not parse theme configuration. Please paste a valid JSON or C# theme configuration.";
        return false;
    }

    public string ExportToCSharp(Theme theme)
    {
        var lightColors = theme.Colors?.Light ?? ThemeColors.DefaultLight;
        var darkColors = theme.Colors?.Dark ?? ThemeColors.DefaultDark;
        return $@"// Add this to your server configuration:
var server = new Server()
    .UseTheme(theme => {{
        theme.Name = ""{theme.Name}"";
        theme.Colors = new ThemeColorScheme
        {{
            Light = new ThemeColors
            {{
                Primary = ""{lightColors.Primary}"",
                PrimaryForeground = ""{lightColors.PrimaryForeground}"",
                Secondary = ""{lightColors.Secondary}"",
                SecondaryForeground = ""{lightColors.SecondaryForeground}"",
                Background = ""{lightColors.Background}"",
                Foreground = ""{lightColors.Foreground}"",
                Destructive = ""{lightColors.Destructive}"",
                DestructiveForeground = ""{lightColors.DestructiveForeground}"",
                Success = ""{lightColors.Success}"",
                SuccessForeground = ""{lightColors.SuccessForeground}"",
                Warning = ""{lightColors.Warning}"",
                WarningForeground = ""{lightColors.WarningForeground}"",
                Info = ""{lightColors.Info}"",
                InfoForeground = ""{lightColors.InfoForeground}"",
                Border = ""{lightColors.Border}"",
                Input = ""{lightColors.Input}"",
                Ring = ""{lightColors.Ring}"",
                Muted = ""{lightColors.Muted}"",
                MutedForeground = ""{lightColors.MutedForeground}"",
                Accent = ""{lightColors.Accent}"",
                AccentForeground = ""{lightColors.AccentForeground}"",
                Card = ""{lightColors.Card}"",
                CardForeground = ""{lightColors.CardForeground}"",
                Popover = ""{lightColors.Popover}"",
                PopoverForeground = ""{lightColors.PopoverForeground}""
            }},
            Dark = new ThemeColors
            {{
                Primary = ""{darkColors.Primary}"",
                PrimaryForeground = ""{darkColors.PrimaryForeground}"",
                Secondary = ""{darkColors.Secondary}"",
                SecondaryForeground = ""{darkColors.SecondaryForeground}"",
                Background = ""{darkColors.Background}"",
                Foreground = ""{darkColors.Foreground}"",
                Destructive = ""{darkColors.Destructive}"",
                DestructiveForeground = ""{darkColors.DestructiveForeground}"",
                Success = ""{darkColors.Success}"",
                SuccessForeground = ""{darkColors.SuccessForeground}"",
                Warning = ""{darkColors.Warning}"",
                WarningForeground = ""{darkColors.WarningForeground}"",
                Info = ""{darkColors.Info}"",
                InfoForeground = ""{darkColors.InfoForeground}"",
                Border = ""{darkColors.Border}"",
                Input = ""{darkColors.Input}"",
                Ring = ""{darkColors.Ring}"",
                Muted = ""{darkColors.Muted}"",
                MutedForeground = ""{darkColors.MutedForeground}"",
                Accent = ""{darkColors.Accent}"",
                AccentForeground = ""{darkColors.AccentForeground}"",
                Card = ""{darkColors.Card}"",
                CardForeground = ""{darkColors.CardForeground}"",
                Popover = ""{darkColors.Popover}"",
                PopoverForeground = ""{darkColors.PopoverForeground}""
            }}
        }};
        theme.FontFamily = ""{theme.FontFamily}"";
        theme.FontSize = ""{theme.FontSize}"";
        theme.BorderRadiusBoxes = ""{theme.BorderRadiusBoxes}"";
        theme.BorderRadiusFields = ""{theme.BorderRadiusFields}"";
        theme.BorderRadiusSelectors = ""{theme.BorderRadiusSelectors}""; 
    }});";
    }

    public string ExportToJson(Theme theme, bool indented = true)
    {
        return JsonSerializer.Serialize(theme, indented ? IndentedExportOptions : CompactExportOptions);
    }

    private static void ApplyColorsFromBlock(string block, ThemeColors target)
    {
        var matches = Regex.Matches(block, @"(?<prop>\w+)\s*=\s*""(?<val>[^""]+)""");
        foreach (Match m in matches)
        {
            var prop = m.Groups["prop"].Value;
            var val = m.Groups["val"].Value;
            switch (prop)
            {
                case "Primary": target.Primary = val; break;
                case "PrimaryForeground": target.PrimaryForeground = val; break;
                case "Secondary": target.Secondary = val; break;
                case "SecondaryForeground": target.SecondaryForeground = val; break;
                case "Background": target.Background = val; break;
                case "Foreground": target.Foreground = val; break;
                case "Destructive": target.Destructive = val; break;
                case "DestructiveForeground": target.DestructiveForeground = val; break;
                case "Success": target.Success = val; break;
                case "SuccessForeground": target.SuccessForeground = val; break;
                case "Warning": target.Warning = val; break;
                case "WarningForeground": target.WarningForeground = val; break;
                case "Info": target.Info = val; break;
                case "InfoForeground": target.InfoForeground = val; break;
                case "Border": target.Border = val; break;
                case "Input": target.Input = val; break;
                case "Ring": target.Ring = val; break;
                case "Muted": target.Muted = val; break;
                case "MutedForeground": target.MutedForeground = val; break;
                case "Accent": target.Accent = val; break;
                case "AccentForeground": target.AccentForeground = val; break;
                case "Card": target.Card = val; break;
                case "CardForeground": target.CardForeground = val; break;
                case "Popover": target.Popover = val; break;
                case "PopoverForeground": target.PopoverForeground = val; break;
            }
        }
    }
}
