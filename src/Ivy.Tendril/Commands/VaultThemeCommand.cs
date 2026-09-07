using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Ivy.Tendril.Themes;
using Spectre.Console;
using Spectre.Console.Cli;
using SpectreValidation = Spectre.Console.ValidationResult;

namespace Ivy.Tendril.Commands;

internal static class VaultThemeCommandHelpers
{
    internal static readonly Dictionary<string, (Func<ThemeColors, string?> Get, Action<ThemeColors, string?> Set)> ColorTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["primary"] = (c => c.Primary, (c, v) => c.Primary = v),
        ["primaryForeground"] = (c => c.PrimaryForeground, (c, v) => c.PrimaryForeground = v),
        ["secondary"] = (c => c.Secondary, (c, v) => c.Secondary = v),
        ["secondaryForeground"] = (c => c.SecondaryForeground, (c, v) => c.SecondaryForeground = v),
        ["background"] = (c => c.Background, (c, v) => c.Background = v),
        ["foreground"] = (c => c.Foreground, (c, v) => c.Foreground = v),
        ["destructive"] = (c => c.Destructive, (c, v) => c.Destructive = v),
        ["destructiveForeground"] = (c => c.DestructiveForeground, (c, v) => c.DestructiveForeground = v),
        ["success"] = (c => c.Success, (c, v) => c.Success = v),
        ["successForeground"] = (c => c.SuccessForeground, (c, v) => c.SuccessForeground = v),
        ["warning"] = (c => c.Warning, (c, v) => c.Warning = v),
        ["warningForeground"] = (c => c.WarningForeground, (c, v) => c.WarningForeground = v),
        ["info"] = (c => c.Info, (c, v) => c.Info = v),
        ["infoForeground"] = (c => c.InfoForeground, (c, v) => c.InfoForeground = v),
        ["border"] = (c => c.Border, (c, v) => c.Border = v),
        ["input"] = (c => c.Input, (c, v) => c.Input = v),
        ["ring"] = (c => c.Ring, (c, v) => c.Ring = v),
        ["muted"] = (c => c.Muted, (c, v) => c.Muted = v),
        ["mutedForeground"] = (c => c.MutedForeground, (c, v) => c.MutedForeground = v),
        ["accent"] = (c => c.Accent, (c, v) => c.Accent = v),
        ["accentForeground"] = (c => c.AccentForeground, (c, v) => c.AccentForeground = v),
        ["card"] = (c => c.Card, (c, v) => c.Card = v),
        ["cardForeground"] = (c => c.CardForeground, (c, v) => c.CardForeground = v),
        ["popover"] = (c => c.Popover, (c, v) => c.Popover = v),
        ["popoverForeground"] = (c => c.PopoverForeground, (c, v) => c.PopoverForeground = v),
    };

    internal static bool IsHexColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && Regex.IsMatch(hex.Trim(), @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$");

    internal static string NormalizeId(string id) =>
        Regex.Replace(id.ToLowerInvariant(), @"[^a-z0-9_-]", "-").Trim('-');

    internal static VaultThemeManifest? FindTheme(List<VaultThemeManifest> themes, string id) =>
        themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    internal static int WriteNotFound(string id, List<VaultThemeManifest> themes)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Theme '{id.EscapeMarkup()}' not found in vault.");
        if (themes.Count > 0)
        {
            AnsiConsole.MarkupLine($"Available: {string.Join(", ", themes.Select(t => t.Id))}");
        }
        else
        {
            AnsiConsole.MarkupLine("Vault contains no themes.");
        }
        return 1;
    }

    internal static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

// --- Settings ---

public class VaultThemeListSettings : CommandSettings
{
    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }
}

public class VaultThemeGetSettings : CommandSettings
{
    [Description("Theme ID to retrieve")]
    [CommandArgument(0, "<theme-id>")]
    public string ThemeId { get; init; } = "";

    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    [Description("Output as JSON")]
    [CommandOption("--json")]
    public bool Json { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ThemeId, "theme-id");
    }
}

public class VaultThemeAddSettings : CommandSettings
{
    [Description("File path containing theme manifest or theme JSON")]
    [CommandOption("-f|--file")]
    public string? FilePath { get; init; }

    [Description("Read theme JSON from stdin")]
    [CommandOption("--stdin")]
    public bool Stdin { get; init; }

    [Description("Optional theme ID override")]
    [CommandOption("--id")]
    public string? Id { get; init; }

    [Description("Optional theme name override")]
    [CommandOption("-n|--name")]
    public string? Name { get; init; }

    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var count = CliValidation.CountSources(Stdin, FilePath, "");
        if (count == 0)
            return SpectreValidation.Error("Provide theme JSON via --file or --stdin.");
        return CliValidation.ValidateSingleSource(count, "--file or --stdin");
    }
}

public class VaultThemeCreateSettings : CommandSettings
{
    [Description("Theme ID")]
    [CommandArgument(0, "<id>")]
    public string Id { get; init; } = "";

    [Description("Display name for the theme")]
    [CommandOption("-n|--name")]
    public string Name { get; init; } = "";

    [Description("Theme description")]
    [CommandOption("-d|--description")]
    public string? Description { get; init; }

    [Description("Base preset theme ID")]
    [CommandOption("-b|--base")]
    public string? BasePreset { get; init; }

    [Description("Create as dark mode theme")]
    [CommandOption("--dark")]
    public bool Dark { get; init; }

    [Description("Create as light mode theme")]
    [CommandOption("--light")]
    public bool Light { get; init; }

    [Description("Primary color hex")]
    [CommandOption("--primary")]
    public string? Primary { get; init; }

    [Description("Secondary color hex")]
    [CommandOption("--secondary")]
    public string? Secondary { get; init; }

    [Description("Accent color hex")]
    [CommandOption("--accent")]
    public string? Accent { get; init; }

    [Description("Background color hex")]
    [CommandOption("--background")]
    public string? Background { get; init; }

    [Description("Foreground color hex")]
    [CommandOption("--foreground")]
    public string? Foreground { get; init; }

    [Description("Color token override (<token>=<hex>)")]
    [CommandOption("--color")]
    public string[]? Colors { get; init; }

    [Description("Font family")]
    [CommandOption("--font")]
    public string? Font { get; init; }

    [Description("Font size")]
    [CommandOption("--font-size")]
    public string? FontSize { get; init; }

    [Description("Border radius for boxes, fields, and selectors")]
    [CommandOption("--radius")]
    public string? Radius { get; init; }

    [Description("Apply overrides to mode: light, dark, or both")]
    [CommandOption("--apply-to")]
    public string? ApplyTo { get; init; }

    [Description("Force creation even if ID collides with built-in theme")]
    [CommandOption("--force")]
    public bool Force { get; init; }

    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var idValidation = CliValidation.RequireNonEmpty(Id, "id");
        if (!idValidation.Successful)
            return idValidation;

        var normalizedId = VaultThemeCommandHelpers.NormalizeId(Id);
        if (string.IsNullOrWhiteSpace(normalizedId))
            return SpectreValidation.Error("<id> normalized to empty. Provide a valid alphanumeric identifier.");

        if (!Force && TendrilThemes.BuiltInThemes.Any(t => string.Equals(t.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
            return SpectreValidation.Error($"Theme id '{Id}' collides with a built-in theme. Use --force to override.");

        var nameValidation = CliValidation.RequireNonEmpty(Name, "name");
        if (!nameValidation.Successful)
            return nameValidation;

        if (!string.IsNullOrWhiteSpace(BasePreset) && !TendrilThemes.BuiltInThemes.Any(t => string.Equals(t.Id, BasePreset, StringComparison.OrdinalIgnoreCase)))
            return SpectreValidation.Error($"Unknown base preset '{BasePreset}'. Valid presets: {string.Join(", ", TendrilThemes.BuiltInThemes.Select(t => t.Id))}");

        if (Dark && Light)
            return SpectreValidation.Error("Cannot specify both --dark and --light.");

        if (!string.IsNullOrWhiteSpace(ApplyTo))
        {
            var applyValidation = CliValidation.ValidateOneOf(ApplyTo, "--apply-to", ["light", "dark", "both"]);
            if (!applyValidation.Successful)
                return applyValidation;
        }

        if (Primary != null && !VaultThemeCommandHelpers.IsHexColor(Primary))
            return SpectreValidation.Error($"Invalid hex color '{Primary}' for --primary.");
        if (Secondary != null && !VaultThemeCommandHelpers.IsHexColor(Secondary))
            return SpectreValidation.Error($"Invalid hex color '{Secondary}' for --secondary.");
        if (Accent != null && !VaultThemeCommandHelpers.IsHexColor(Accent))
            return SpectreValidation.Error($"Invalid hex color '{Accent}' for --accent.");
        if (Background != null && !VaultThemeCommandHelpers.IsHexColor(Background))
            return SpectreValidation.Error($"Invalid hex color '{Background}' for --background.");
        if (Foreground != null && !VaultThemeCommandHelpers.IsHexColor(Foreground))
            return SpectreValidation.Error($"Invalid hex color '{Foreground}' for --foreground.");

        if (Colors != null)
        {
            foreach (var c in Colors)
            {
                var eq = c.IndexOf('=');
                if (eq <= 0 || eq >= c.Length - 1)
                    return SpectreValidation.Error($"Invalid color override '{c}'. Expected '<token>=<hex>'.");
                var token = c[..eq].Trim();
                var hex = c[(eq + 1)..].Trim();
                if (!VaultThemeCommandHelpers.ColorTokens.ContainsKey(token))
                    return SpectreValidation.Error($"Unknown color token '{token}' in --color '{c}'.");
                if (!VaultThemeCommandHelpers.IsHexColor(hex))
                    return SpectreValidation.Error($"Invalid hex color '{hex}' in --color '{c}'.");
            }
        }

        return SpectreValidation.Success();
    }
}

public class VaultThemeSetSettings : CommandSettings
{
    public static readonly string[] ValidStandardFields =
    [
        "name", "description", "mode", "font", "font-size",
        "radius-boxes", "radius-fields", "radius-selectors",
        "shadow-boxes", "shadow-fields", "shadow-selectors", "preview"
    ];

    [Description("Theme ID")]
    [CommandArgument(0, "<id>")]
    public string Id { get; init; } = "";

    [Description("Field or color token to update")]
    [CommandArgument(1, "<field>")]
    public string Field { get; init; } = "";

    [Description("New value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; init; } = "";

    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var idValidation = CliValidation.RequireNonEmpty(Id, "id");
        if (!idValidation.Successful)
            return idValidation;

        if (string.IsNullOrWhiteSpace(Field))
            return SpectreValidation.Error($"<field> is required and cannot be empty. Valid fields: {string.Join(", ", ValidStandardFields)}");

        var isStandard = ValidStandardFields.Contains(Field, StringComparer.OrdinalIgnoreCase);
        var isColor = false;
        if (!isStandard)
        {
            var token = Field;
            if (token.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
                token = token[6..];
            else if (token.StartsWith("dark.", StringComparison.OrdinalIgnoreCase))
                token = token[5..];

            isColor = VaultThemeCommandHelpers.ColorTokens.ContainsKey(token);
        }

        if (!isStandard && !isColor)
            return SpectreValidation.Error($"Unknown field '{Field}'. Valid fields: {string.Join(", ", ValidStandardFields)}");

        if (isColor && !VaultThemeCommandHelpers.IsHexColor(Value))
            return SpectreValidation.Error($"Invalid hex color '{Value}' for color field '{Field}'. Expected format: #RGB, #RRGGBB, or #RRGGBBAA.");

        if (Field.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            var parts = Value.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 4 || parts.Any(p => !VaultThemeCommandHelpers.IsHexColor(p)))
                return SpectreValidation.Error($"Invalid preview colors '{Value}'. Expected four comma-separated hex colors (e.g. '#111,#222,#333,#444').");
        }

        if (Field.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            var modeValidation = CliValidation.ValidateOneOf(Value, "mode", ["light", "dark"]);
            if (!modeValidation.Successful)
                return modeValidation;
        }

        if (Field.Equals("shadow-boxes", StringComparison.OrdinalIgnoreCase) ||
            Field.Equals("shadow-fields", StringComparison.OrdinalIgnoreCase) ||
            Field.Equals("shadow-selectors", StringComparison.OrdinalIgnoreCase))
        {
            if (!VaultCommandHelpers.TryParseBool(Value, out _))
                return SpectreValidation.Error($"Invalid boolean value '{Value}' for '{Field}'. Expected true, false, yes, or no.");
        }

        return SpectreValidation.Success();
    }
}

public class VaultThemeDeleteSettings : CommandSettings
{
    [Description("Theme ID to delete")]
    [CommandArgument(0, "<theme-id>")]
    public string ThemeId { get; init; } = "";

    [Description("Target vault ID")]
    [CommandOption("--vault")]
    public string? VaultId { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ThemeId, "theme-id");
    }
}

public class VaultThemeApplySettings : CommandSettings
{
    [Description("Theme ID to apply")]
    [CommandArgument(0, "<theme-id>")]
    public string ThemeId { get; init; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(ThemeId, "theme-id");
    }
}

// --- Commands ---

public class VaultThemeListCommand(IVaultService vaultService) : AsyncCommand<VaultThemeListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeListSettings settings, CancellationToken cancellationToken)
    {
        var themes = await vaultService.GetThemesAsync(settings.VaultId);

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(themes, VaultThemeCommandHelpers.ManifestJsonOptions));
            return 0;
        }

        if (themes.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No themes found in vault.[/]");
            return 0;
        }

        var rows = themes.Select(t => (IReadOnlyList<string>)new[]
        {
            t.Id,
            t.Name,
            t.IsDark ? "Dark" : "Light",
            t.Description,
            string.Join(", ", t.PreviewColors ?? []),
            t.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            t.UpdatedBy ?? "-"
        });

        CliOutput.WriteTable(["Id", "Name", "Mode", "Description", "Preview", "Updated", "Updated By"], rows);
        return 0;
    }
}

public class VaultThemeGetCommand(IVaultService vaultService) : AsyncCommand<VaultThemeGetSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeGetSettings settings, CancellationToken cancellationToken)
    {
        var themes = await vaultService.GetThemesAsync(settings.VaultId);
        var theme = VaultThemeCommandHelpers.FindTheme(themes, settings.ThemeId);
        if (theme == null)
        {
            return VaultThemeCommandHelpers.WriteNotFound(settings.ThemeId, themes);
        }

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(theme, VaultThemeCommandHelpers.ManifestJsonOptions));
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold]Id:[/] {theme.Id.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Name:[/] {theme.Name.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Description:[/] {(string.IsNullOrWhiteSpace(theme.Description) ? "-" : theme.Description.EscapeMarkup())}");
        AnsiConsole.MarkupLine($"[bold]Mode:[/] {(theme.IsDark ? "Dark" : "Light")}");
        AnsiConsole.MarkupLine($"[bold]Preview:[/] {string.Join(", ", theme.PreviewColors ?? [])}");
        AnsiConsole.MarkupLine($"[bold]Font:[/] {(string.IsNullOrWhiteSpace(theme.IvyTheme.FontFamily) ? "-" : theme.IvyTheme.FontFamily.EscapeMarkup())}");
        AnsiConsole.MarkupLine($"[bold]Font Size:[/] {(string.IsNullOrWhiteSpace(theme.IvyTheme.FontSize) ? "-" : theme.IvyTheme.FontSize.EscapeMarkup())}");
        AnsiConsole.MarkupLine($"[bold]Border radii:[/] Boxes={theme.IvyTheme.BorderRadiusBoxes ?? "-"}, Fields={theme.IvyTheme.BorderRadiusFields ?? "-"}, Selectors={theme.IvyTheme.BorderRadiusSelectors ?? "-"}");
        AnsiConsole.MarkupLine($"[bold]Shadows:[/] Boxes={theme.IvyTheme.ShadowBoxes}, Fields={theme.IvyTheme.ShadowFields}, Selectors={theme.IvyTheme.ShadowSelectors}");
        AnsiConsole.MarkupLine($"[bold]Updated:[/] {theme.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        AnsiConsole.MarkupLine($"[bold]Updated By:[/] {(string.IsNullOrWhiteSpace(theme.UpdatedBy) ? "-" : theme.UpdatedBy.EscapeMarkup())}");

        var lightColors = theme.IvyTheme.Colors?.Light ?? ThemeColors.DefaultLight;
        var darkColors = theme.IvyTheme.Colors?.Dark ?? ThemeColors.DefaultDark;

        var colorRows = VaultThemeCommandHelpers.ColorTokens.Select(kvp => (IReadOnlyList<string>)new[]
        {
            kvp.Key,
            kvp.Value.Get(lightColors) ?? "-",
            kvp.Value.Get(darkColors) ?? "-"
        });

        CliOutput.WriteTable(["Token", "Light", "Dark"], colorRows);
        return 0;
    }
}

public class VaultThemeAddCommand(IVaultService vaultService, IThemeSerializationService? themeSerializationService = null) : AsyncCommand<VaultThemeAddSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeAddSettings settings, CancellationToken cancellationToken)
    {
        var serializer = themeSerializationService ?? ThemeSerializationService.Default;
        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, "");
        if (string.IsNullOrWhiteSpace(content))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Theme content cannot be empty.");
            return 1;
        }

        VaultThemeManifest? manifest = null;
        var deserializeOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        try
        {
            manifest = JsonSerializer.Deserialize<VaultThemeManifest>(content, deserializeOptions);
        }
        catch
        {
            // Fall through to TryImportTheme
        }

        if (manifest?.IvyTheme?.Colors == null || (manifest.IvyTheme.Colors.Light == null && manifest.IvyTheme.Colors.Dark == null))
        {
            if (serializer.TryImportTheme(content, out var importedTheme, out var importError))
            {
                manifest = new VaultThemeManifest
                {
                    Id = settings.Id ?? "",
                    Name = settings.Name ?? importedTheme.Name,
                    Description = "",
                    IsDark = false,
                    PreviewColors = TendrilThemes.ExtractPreviewColors(importedTheme, false),
                    IvyTheme = importedTheme
                };
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Failed to parse theme: {(importError ?? "Invalid theme format").EscapeMarkup()}");
                return 1;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.Id))
            manifest.Id = settings.Id;
        if (!string.IsNullOrWhiteSpace(settings.Name))
        {
            manifest.Name = settings.Name;
            manifest.IvyTheme.Name = settings.Name;
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Theme name cannot be empty.");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            manifest.Id = VaultThemeCommandHelpers.NormalizeId(manifest.Name);
        }
        else
        {
            manifest.Id = VaultThemeCommandHelpers.NormalizeId(manifest.Id);
        }

        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Theme id cannot be empty.");
            return 1;
        }

        var result = await vaultService.SaveThemeToVaultAsync(manifest, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Theme '{manifest.Id.EscapeMarkup()}' added to vault successfully.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {result.Message.EscapeMarkup()}{(result.ErrorMessage != null ? $": {result.ErrorMessage.EscapeMarkup()}" : "")}");
            return 1;
        }
    }
}

public class VaultThemeCreateCommand(IVaultService vaultService) : AsyncCommand<VaultThemeCreateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeCreateSettings settings, CancellationToken cancellationToken)
    {
        Theme theme;
        bool isDark;

        if (!string.IsNullOrWhiteSpace(settings.BasePreset))
        {
            var basePreset = TendrilThemes.GetTheme(settings.BasePreset);
            theme = TendrilThemes.CloneTheme(basePreset.IvyTheme);
            isDark = basePreset.IsDark;
        }
        else
        {
            theme = TendrilThemes.CloneTheme(Theme.Default);
            isDark = false;
        }

        if (settings.Dark) isDark = true;
        if (settings.Light) isDark = false;

        theme.Colors ??= new ThemeColorScheme();
        theme.Colors.Light ??= TendrilThemes.CloneThemeColors(ThemeColors.DefaultLight);
        theme.Colors.Dark ??= TendrilThemes.CloneThemeColors(ThemeColors.DefaultDark);

        var targetModes = new List<ThemeColors>();
        var applyTo = settings.ApplyTo?.ToLowerInvariant();
        if (applyTo == "both")
        {
            targetModes.Add(theme.Colors.Light);
            targetModes.Add(theme.Colors.Dark);
        }
        else if (applyTo == "light")
        {
            targetModes.Add(theme.Colors.Light);
        }
        else if (applyTo == "dark")
        {
            targetModes.Add(theme.Colors.Dark);
        }
        else
        {
            targetModes.Add(isDark ? theme.Colors.Dark : theme.Colors.Light);
        }

        foreach (var target in targetModes)
        {
            if (settings.Primary != null) target.Primary = settings.Primary;
            if (settings.Secondary != null) target.Secondary = settings.Secondary;
            if (settings.Accent != null) target.Accent = settings.Accent;
            if (settings.Background != null) target.Background = settings.Background;
            if (settings.Foreground != null) target.Foreground = settings.Foreground;

            if (settings.Colors != null)
            {
                foreach (var c in settings.Colors)
                {
                    var eq = c.IndexOf('=');
                    if (eq > 0)
                    {
                        var token = c[..eq].Trim();
                        var hex = c[(eq + 1)..].Trim();
                        if (VaultThemeCommandHelpers.ColorTokens.TryGetValue(token, out var accessor))
                        {
                            accessor.Set(target, hex);
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.Font)) theme.FontFamily = settings.Font;
        if (!string.IsNullOrWhiteSpace(settings.FontSize)) theme.FontSize = settings.FontSize;
        if (!string.IsNullOrWhiteSpace(settings.Radius))
        {
            theme.BorderRadiusBoxes = settings.Radius;
            theme.BorderRadiusFields = settings.Radius;
            theme.BorderRadiusSelectors = settings.Radius;
        }
        theme.Name = settings.Name;

        var curColors = isDark ? theme.Colors.Dark : theme.Colors.Light;
        string[] previewColors =
        [
            curColors.Primary ?? "#18181b",
            curColors.Secondary ?? "#71717a",
            curColors.Accent ?? "#27272a",
            curColors.Background ?? "#ffffff"
        ];

        var normalizedId = VaultThemeCommandHelpers.NormalizeId(settings.Id);
        var manifest = new VaultThemeManifest
        {
            Id = normalizedId,
            Name = settings.Name,
            Description = settings.Description ?? "",
            IsDark = isDark,
            PreviewColors = previewColors,
            IvyTheme = theme
        };

        var result = await vaultService.SaveThemeToVaultAsync(manifest, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Theme '{manifest.Id.EscapeMarkup()}' created and saved to vault.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {result.Message.EscapeMarkup()}");
            return 1;
        }
    }
}

public class VaultThemeSetCommand(IVaultService vaultService) : AsyncCommand<VaultThemeSetSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeSetSettings settings, CancellationToken cancellationToken)
    {
        var themes = await vaultService.GetThemesAsync(settings.VaultId);
        var theme = VaultThemeCommandHelpers.FindTheme(themes, settings.Id);
        if (theme == null)
        {
            return VaultThemeCommandHelpers.WriteNotFound(settings.Id, themes);
        }

        var field = settings.Field;
        if (field.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            theme.Name = settings.Value;
            theme.IvyTheme.Name = settings.Value;
        }
        else if (field.Equals("description", StringComparison.OrdinalIgnoreCase))
        {
            theme.Description = settings.Value;
        }
        else if (field.Equals("mode", StringComparison.OrdinalIgnoreCase))
        {
            theme.IsDark = settings.Value.Equals("dark", StringComparison.OrdinalIgnoreCase);
        }
        else if (field.Equals("font", StringComparison.OrdinalIgnoreCase))
        {
            theme.IvyTheme.FontFamily = settings.Value;
        }
        else if (field.Equals("font-size", StringComparison.OrdinalIgnoreCase))
        {
            theme.IvyTheme.FontSize = settings.Value;
        }
        else if (field.Equals("radius-boxes", StringComparison.OrdinalIgnoreCase))
        {
            theme.IvyTheme.BorderRadiusBoxes = settings.Value;
        }
        else if (field.Equals("radius-fields", StringComparison.OrdinalIgnoreCase))
        {
            theme.IvyTheme.BorderRadiusFields = settings.Value;
        }
        else if (field.Equals("radius-selectors", StringComparison.OrdinalIgnoreCase))
        {
            theme.IvyTheme.BorderRadiusSelectors = settings.Value;
        }
        else if (field.Equals("shadow-boxes", StringComparison.OrdinalIgnoreCase))
        {
            if (VaultCommandHelpers.TryParseBool(settings.Value, out var val))
                theme.IvyTheme.ShadowBoxes = val;
        }
        else if (field.Equals("shadow-fields", StringComparison.OrdinalIgnoreCase))
        {
            if (VaultCommandHelpers.TryParseBool(settings.Value, out var val))
                theme.IvyTheme.ShadowFields = val;
        }
        else if (field.Equals("shadow-selectors", StringComparison.OrdinalIgnoreCase))
        {
            if (VaultCommandHelpers.TryParseBool(settings.Value, out var val))
                theme.IvyTheme.ShadowSelectors = val;
        }
        else if (field.Equals("preview", StringComparison.OrdinalIgnoreCase))
        {
            theme.PreviewColors = settings.Value.Split(',', StringSplitOptions.TrimEntries);
        }
        else
        {
            theme.IvyTheme.Colors ??= new ThemeColorScheme();
            theme.IvyTheme.Colors.Light ??= TendrilThemes.CloneThemeColors(ThemeColors.DefaultLight);
            theme.IvyTheme.Colors.Dark ??= TendrilThemes.CloneThemeColors(ThemeColors.DefaultDark);

            ThemeColors target;
            string token;
            if (field.StartsWith("light.", StringComparison.OrdinalIgnoreCase))
            {
                target = theme.IvyTheme.Colors.Light;
                token = field[6..];
            }
            else if (field.StartsWith("dark.", StringComparison.OrdinalIgnoreCase))
            {
                target = theme.IvyTheme.Colors.Dark;
                token = field[5..];
            }
            else
            {
                target = theme.IsDark ? theme.IvyTheme.Colors.Dark : theme.IvyTheme.Colors.Light;
                token = field;
            }

            if (VaultThemeCommandHelpers.ColorTokens.TryGetValue(token, out var accessor))
            {
                accessor.Set(target, settings.Value);
            }
        }

        var result = await vaultService.SaveThemeToVaultAsync(theme, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"Updated {settings.Field} on theme '{theme.Id.EscapeMarkup()}'.");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {result.Message.EscapeMarkup()}");
            return 1;
        }
    }
}

public class VaultThemeDeleteCommand(IVaultService vaultService) : AsyncCommand<VaultThemeDeleteSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, VaultThemeDeleteSettings settings, CancellationToken cancellationToken)
    {
        var themes = await vaultService.GetThemesAsync(settings.VaultId);
        var theme = VaultThemeCommandHelpers.FindTheme(themes, settings.ThemeId);
        if (theme == null)
        {
            return VaultThemeCommandHelpers.WriteNotFound(settings.ThemeId, themes);
        }

        var result = await vaultService.DeleteThemeFromVaultAsync(theme.Id, settings.VaultId);
        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]Theme '{theme.Id.EscapeMarkup()}' deleted from vault successfully.[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {result.Message.EscapeMarkup()}");
            return 1;
        }
    }
}

public class VaultThemeApplyCommand(IVaultService vaultService, ConfigService config) : AsyncCommand<VaultThemeApplySettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, VaultThemeApplySettings settings, CancellationToken cancellationToken)
    {
        vaultService.LoadThemesIntoRegistry();

        string resolvedId;
        try
        {
            resolvedId = ConfigSetCommand.ValidateTheme(settings.ThemeId);
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
            return Task.FromResult(1);
        }

        config.MutateAndSave(s => s.Theme = resolvedId);
        AnsiConsole.MarkupLine($"Active theme set to '{resolvedId.EscapeMarkup()}'.");
        return Task.FromResult(0);
    }
}
