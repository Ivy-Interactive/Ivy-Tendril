using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Vault;
using Ivy.Tendril.Themes;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class VaultThemesDialog(
    IState<bool> dialogOpen,
    IVaultService vaultService,
    IClientProvider client,
    IConfigService config,
    string? vaultId,
    Action onThemesUpdated) : ViewBase
{
    public override object? Build()
    {
        var activeTab = UseState("themes");
        var isSaving = UseState(false);
        var isDeleting = UseState<string?>(null);

        var themesQuery = UseQuery<List<VaultThemeManifest>, string>(
            $"vault_themes_{vaultId}",
            async (_, _) => await vaultService.GetThemesAsync(vaultId));

        // Generator states
        var themeName = UseState("Team Brand");
        var themeDesc = UseState("Custom team vault theme");
        var selectedPreset = UseState("Default");
        var selectedMode = UseState("light"); // "light" or "dark"

        var fontFamily = UseState("Geist");
        var fontSize = UseState("16px");
        var radiusBoxes = UseState("0.5rem");
        var radiusFields = UseState("0.5rem");
        var radiusSelectors = UseState("0.5rem");

        var editingTheme = UseState(() => CloneTheme(TendrilThemes.Default.IvyTheme));

        // When preset is selected, copy preset theme into editingTheme
        UseEffect(() =>
        {
            var preset = TendrilThemes.BuiltInThemes.FirstOrDefault(p => p.Name.Equals(selectedPreset.Value, StringComparison.OrdinalIgnoreCase))
                         ?? TendrilThemes.Default;
            editingTheme.Set(CloneTheme(preset.IvyTheme));
            if (!string.IsNullOrEmpty(preset.IvyTheme.FontFamily))
                fontFamily.Set(preset.IvyTheme.FontFamily);
            if (!string.IsNullOrEmpty(preset.IvyTheme.BorderRadiusBoxes))
                radiusBoxes.Set(preset.IvyTheme.BorderRadiusBoxes);
            if (!string.IsNullOrEmpty(preset.IvyTheme.BorderRadiusFields))
                radiusFields.Set(preset.IvyTheme.BorderRadiusFields);
            if (!string.IsNullOrEmpty(preset.IvyTheme.BorderRadiusSelectors))
                radiusSelectors.Set(preset.IvyTheme.BorderRadiusSelectors);
        }, selectedPreset);

        if (!dialogOpen.Value) return null;

        var themesList = themesQuery.Value ?? new List<VaultThemeManifest>();

        void UpdateColor(Action<ThemeColors> updateAction)
        {
            var clone = CloneTheme(editingTheme.Value);
            var colors = selectedMode.Value == "light" ? clone.Colors.Light : clone.Colors.Dark;
            updateAction(colors);
            editingTheme.Set(clone);
        }

        async Task HandleSaveToVault()
        {
            if (isSaving.Value) return;

            var rawName = themeName.Value.Trim();
            if (string.IsNullOrWhiteSpace(rawName))
            {
                client.Toast("Please provide a theme name", "Validation Error").Destructive();
                return;
            }

            isSaving.Set(true);

            var id = Regex.Replace(rawName.ToLowerInvariant(), @"[^a-z0-9_-]", "-").Trim('-');
            if (string.IsNullOrWhiteSpace(id))
            {
                id = "vault-theme-" + Guid.NewGuid().ToString("N")[..6];
            }

            var currentColors = selectedMode.Value == "light"
                ? editingTheme.Value.Colors.Light
                : editingTheme.Value.Colors.Dark;

            var manifest = new VaultThemeManifest
            {
                Id = id,
                Name = rawName,
                Description = themeDesc.Value.Trim(),
                IsDark = selectedMode.Value == "dark",
                PreviewColors =
                [
                    currentColors.Primary ?? "#18181b",
                    currentColors.Secondary ?? "#71717a",
                    currentColors.Accent ?? "#27272a",
                    currentColors.Background ?? "#ffffff"
                ],
                IvyTheme = CloneTheme(editingTheme.Value)
            };

            manifest.IvyTheme.Name = manifest.Name;
            manifest.IvyTheme.FontFamily = string.IsNullOrWhiteSpace(fontFamily.Value) ? null : fontFamily.Value;
            manifest.IvyTheme.FontSize = string.IsNullOrWhiteSpace(fontSize.Value) ? null : fontSize.Value;
            manifest.IvyTheme.BorderRadiusBoxes = radiusBoxes.Value;
            manifest.IvyTheme.BorderRadiusFields = radiusFields.Value;
            manifest.IvyTheme.BorderRadiusSelectors = radiusSelectors.Value;

            var result = await vaultService.SaveThemeToVaultAsync(manifest, vaultId);
            isSaving.Set(false);

            if (result.Success)
            {
                client.Toast(result.Message, "Vault Theme Saved");
                themesQuery.Mutator.Revalidate();
                onThemesUpdated();
                activeTab.Set("themes");
            }
            else
            {
                client.Toast(result.ErrorMessage ?? result.Message, "Save Failed").Destructive();
            }
        }

        async Task HandleDelete(string themeId)
        {
            if (isDeleting.Value != null) return;
            isDeleting.Set(themeId);

            var result = await vaultService.DeleteThemeFromVaultAsync(themeId, vaultId);
            isDeleting.Set(null);

            if (result.Success)
            {
                client.Toast(result.Message, "Theme Deleted");
                themesQuery.Mutator.Revalidate();
                onThemesUpdated();
            }
            else
            {
                client.Toast(result.ErrorMessage ?? result.Message, "Delete Failed").Destructive();
            }
        }

        void LoadForEdit(VaultThemeManifest t)
        {
            themeName.Set(t.Name);
            themeDesc.Set(t.Description);
            if (t.IvyTheme != null)
            {
                editingTheme.Set(CloneTheme(t.IvyTheme));
                if (!string.IsNullOrEmpty(t.IvyTheme.FontFamily))
                    fontFamily.Set(t.IvyTheme.FontFamily);
                if (!string.IsNullOrEmpty(t.IvyTheme.BorderRadiusBoxes))
                    radiusBoxes.Set(t.IvyTheme.BorderRadiusBoxes);
                if (!string.IsNullOrEmpty(t.IvyTheme.BorderRadiusFields))
                    radiusFields.Set(t.IvyTheme.BorderRadiusFields);
                if (!string.IsNullOrEmpty(t.IvyTheme.BorderRadiusSelectors))
                    radiusSelectors.Set(t.IvyTheme.BorderRadiusSelectors);
            }
            selectedMode.Set(t.IsDark ? "dark" : "light");
            activeTab.Set("generator");
        }

        // --- TAB 1: Existing Vault Themes ---
        object themesTabContent;
        if (themesQuery.Loading && themesList.Count == 0)
        {
            themesTabContent = Layout.Vertical().AlignContent(Align.Left)
                | Text.Muted("Loading vault themes...");
        }
        else if (themesList.Count == 0)
        {
            themesTabContent = Layout.Vertical().AlignContent(Align.Left)
                | Text.Block("No Custom Themes in Vault").Bold()
                | Text.P("This team vault doesn't have any custom themes yet. Use the Theme Generator tab to create and upload one.").Small().Muted()
                | new Button("Create Custom Theme")
                    .Icon(Icons.Plus)
                    .Primary()
                    .OnClick(() => activeTab.Set("generator"));
        }
        else
        {
            var rows = themesList.Select(t =>
            {
                var swatches = Layout.Horizontal()
                    | (t.PreviewColors ?? []).Select(c =>
                        new Svg($"<svg width='18' height='18' viewBox='0 0 18 18'><circle cx='9' cy='9' r='8' fill='{c}' stroke='rgba(128,128,128,0.3)' stroke-width='1.5'/></svg>")
                            .Width(Size.Px(18))
                            .Height(Size.Px(18))
                    ).ToArray();

                var isCurrentTheme = string.Equals(config.Settings.Theme, t.Id, StringComparison.OrdinalIgnoreCase);

                var actions = Layout.Horizontal().AlignContent(Align.Right)
                    | (isCurrentTheme
                        ? new Badge("Active").Variant(BadgeVariant.Secondary).Small()
                        : new Button("Apply")
                            .Icon(Icons.Check)
                            .Outline()
                            .Small()
                            .OnClick(() =>
                            {
                                TendrilThemes.ApplyTheme(client, t.Id);
                                config.Settings.Theme = t.Id;
                                config.SaveSettings();
                                client.Toast($"Theme set to {t.Name}", "Theme Applied");
                            }))
                    | new Button("Edit")
                        .Icon(Icons.Pen)
                        .Outline()
                        .Small()
                        .OnClick(() => LoadForEdit(t))
                    | new Button()
                        .Icon(Icons.Trash2)
                        .Destructive()
                        .Ghost()
                        .Small()
                        .Loading(isDeleting.Value == t.Id)
                        .OnClick(async () => await HandleDelete(t.Id));

                return Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | (Layout.Vertical().AlignContent(Align.Left)
                        | (Layout.Horizontal().AlignContent(Align.Left)
                            | Text.Block(t.Name).Bold()
                            | (t.IsDark ? new Badge("Dark").Variant(BadgeVariant.Outline).Small() : new Badge("Light").Variant(BadgeVariant.Outline).Small()))
                        | Text.P(!string.IsNullOrWhiteSpace(t.Description) ? t.Description : "Custom theme").Small().Muted()
                        | swatches)
                    | actions;
            }).ToArray();

            themesTabContent = Layout.Vertical()
                | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | Text.Block($"{themesList.Count} Custom {(themesList.Count == 1 ? "Theme" : "Themes")} in Vault").Bold().Small()
                    | new Button("New Custom Theme")
                        .Icon(Icons.Plus)
                        .Primary()
                        .Small()
                        .OnClick(() => activeTab.Set("generator")))
                | new Separator()
                | rows;
        }

        // --- TAB 2: Theme Generator ---
        var curColors = selectedMode.Value == "light"
            ? editingTheme.Value.Colors.Light
            : editingTheme.Value.Colors.Dark;

        var presetOptions = TendrilThemes.BuiltInThemes
            .Select(p => new Option<string>(p.Name, p.Name))
            .ToArray();

        var radiusOptions = new[]
        {
            new Option<string>("Sharp (0px)", "0px"),
            new Option<string>("Subtle (0.25rem)", "0.25rem"),
            new Option<string>("Rounded (0.5rem)", "0.5rem"),
            new Option<string>("Pill (1rem)", "1rem"),
            new Option<string>("Full (1.5rem)", "1.5rem")
        };

        // Live Swatches Preview
        var livePreviewSwatches = Layout.Horizontal()
            | new[] { curColors.Primary, curColors.Secondary, curColors.Accent, curColors.Background, curColors.Foreground }
                .Select(c =>
                    new Svg($"<svg width='22' height='22' viewBox='0 0 22 22'><circle cx='11' cy='11' r='10' fill='{c ?? "#888888"}' stroke='rgba(128,128,128,0.3)' stroke-width='1.5'/></svg>")
                        .Width(Size.Px(22))
                        .Height(Size.Px(22))
                ).ToArray();

        var liveSampleCard = Layout.Vertical().AlignContent(Align.Left)
            | Text.Block("Live Theme Preview").Bold().Small()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | livePreviewSwatches
                | (selectedMode.Value == "dark" ? new Badge("Dark Mode").Variant(BadgeVariant.Secondary).Small() : new Badge("Light Mode").Variant(BadgeVariant.Secondary).Small()))
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Primary Button").Primary().Small()
                | new Button("Outline Button").Outline().Small()
                | new Button("Destructive").Destructive().Small()
                | new Badge("Accent Badge").Variant(BadgeVariant.Secondary).Small());

        var generatorTabContent = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                | Text.Block("Theme Generator").Bold()
                | (Layout.Horizontal().AlignContent(Align.Right)
                    | new Button("Light")
                        .Variant(selectedMode.Value == "light" ? ButtonVariant.Primary : ButtonVariant.Outline)
                        .Icon(Icons.Sun)
                        .Small()
                        .OnClick(() => selectedMode.Set("light"))
                    | new Button("Dark")
                        .Variant(selectedMode.Value == "dark" ? ButtonVariant.Primary : ButtonVariant.Outline)
                        .Icon(Icons.Moon)
                        .Small()
                        .OnClick(() => selectedMode.Set("dark"))))
            | (Layout.Horizontal().AlignContent(Align.Left)
                | themeName.ToTextInput("Theme Name").WithField().Label("Name").Width(Size.Units(60))
                | themeDesc.ToTextInput("Theme Description").WithField().Label("Description").Width(Size.Units(70))
                | selectedPreset.ToSelectInput(presetOptions).WithField().Label("Base Preset").Width(Size.Units(50)))
            | new Separator()
            | liveSampleCard
            | new Separator()
            | Text.Block("Brand & Base Colors").Bold().Small()
            | (Layout.Grid().Columns(4)
                | BuildColorField("Primary", curColors.Primary, c => UpdateColor(x => x.Primary = c))
                | BuildColorField("Primary Fg", curColors.PrimaryForeground, c => UpdateColor(x => x.PrimaryForeground = c))
                | BuildColorField("Secondary", curColors.Secondary, c => UpdateColor(x => x.Secondary = c))
                | BuildColorField("Secondary Fg", curColors.SecondaryForeground, c => UpdateColor(x => x.SecondaryForeground = c))
                | BuildColorField("Accent", curColors.Accent, c => UpdateColor(x => x.Accent = c))
                | BuildColorField("Accent Fg", curColors.AccentForeground, c => UpdateColor(x => x.AccentForeground = c))
                | BuildColorField("Background", curColors.Background, c => UpdateColor(x => x.Background = c))
                | BuildColorField("Foreground", curColors.Foreground, c => UpdateColor(x => x.Foreground = c)))
            | new Separator()
            | Text.Block("Surfaces & Borders").Bold().Small()
            | (Layout.Grid().Columns(4)
                | BuildColorField("Card", curColors.Card, c => UpdateColor(x => x.Card = c))
                | BuildColorField("Card Fg", curColors.CardForeground, c => UpdateColor(x => x.CardForeground = c))
                | BuildColorField("Muted", curColors.Muted, c => UpdateColor(x => x.Muted = c))
                | BuildColorField("Muted Fg", curColors.MutedForeground, c => UpdateColor(x => x.MutedForeground = c))
                | BuildColorField("Border", curColors.Border, c => UpdateColor(x => x.Border = c))
                | BuildColorField("Input", curColors.Input, c => UpdateColor(x => x.Input = c))
                | BuildColorField("Ring", curColors.Ring, c => UpdateColor(x => x.Ring = c))
                | BuildColorField("Popover", curColors.Popover, c => UpdateColor(x => x.Popover = c)))
            | new Separator()
            | Text.Block("Semantic Colors").Bold().Small()
            | (Layout.Grid().Columns(4)
                | BuildColorField("Success", curColors.Success, c => UpdateColor(x => x.Success = c))
                | BuildColorField("Success Fg", curColors.SuccessForeground, c => UpdateColor(x => x.SuccessForeground = c))
                | BuildColorField("Destructive", curColors.Destructive, c => UpdateColor(x => x.Destructive = c))
                | BuildColorField("Destructive Fg", curColors.DestructiveForeground, c => UpdateColor(x => x.DestructiveForeground = c))
                | BuildColorField("Warning", curColors.Warning, c => UpdateColor(x => x.Warning = c))
                | BuildColorField("Warning Fg", curColors.WarningForeground, c => UpdateColor(x => x.WarningForeground = c))
                | BuildColorField("Info", curColors.Info, c => UpdateColor(x => x.Info = c))
                | BuildColorField("Info Fg", curColors.InfoForeground, c => UpdateColor(x => x.InfoForeground = c)))
            | new Separator()
            | Text.Block("Typography & Layout").Bold().Small()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | fontFamily.ToTextInput("e.g. Geist, Inter").WithField().Label("Font Family").Width(Size.Units(50))
                | fontSize.ToTextInput("16px").WithField().Label("Font Size").Width(Size.Units(35))
                | radiusBoxes.ToSelectInput(radiusOptions).WithField().Label("Radius (Boxes)").Width(Size.Units(45))
                | radiusFields.ToSelectInput(radiusOptions).WithField().Label("Radius (Fields)").Width(Size.Units(45)));

        var tabs = Layout.Tabs(
            new Tab("Vault Themes", themesTabContent).Icon(Icons.Palette),
            new Tab("Theme Generator", generatorTabContent).Icon(Icons.SlidersHorizontal)
        );

        var dialogActions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Close").Outline().OnClick(() => dialogOpen.Set(false))
            | new Button("Upload to Team Vault")
                .Icon(Icons.Upload)
                .Primary()
                .Loading(isSaving.Value)
                .Disabled(isSaving.Value || string.IsNullOrWhiteSpace(themeName.Value))
                .OnClick(async () => await HandleSaveToVault());

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader("Team Vault Themes"),
            new DialogBody(tabs),
            new DialogFooter(dialogActions)
        ).Width(Size.Units(190));
    }

    private static object BuildColorField(string label, string? color, Action<string> onChange)
    {
        var val = !string.IsNullOrWhiteSpace(color) ? color : "#000000";
        return Layout.Vertical().AlignContent(Align.Left)
            | Text.Block(label).Small().Muted()
            | new Ivy.Widgets.Internal.ThemeColorPicker(val, e =>
            {
                if (!string.IsNullOrWhiteSpace(e.Value))
                {
                    onChange(e.Value);
                }
            }, placeholder: label);
    }

    private static Theme CloneTheme(Theme source)
    {
        return new Theme
        {
            Name = source.Name,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            BorderRadiusBoxes = source.BorderRadiusBoxes,
            BorderRadiusFields = source.BorderRadiusFields,
            BorderRadiusSelectors = source.BorderRadiusSelectors,
            ShadowBoxes = source.ShadowBoxes,
            ShadowFields = source.ShadowFields,
            ShadowSelectors = source.ShadowSelectors,
            Colors = new ThemeColorScheme
            {
                Light = CloneThemeColors(source.Colors?.Light ?? ThemeColors.DefaultLight),
                Dark = CloneThemeColors(source.Colors?.Dark ?? ThemeColors.DefaultDark)
            }
        };
    }

    private static ThemeColors CloneThemeColors(ThemeColors source)
    {
        return new ThemeColors
        {
            Primary = source.Primary,
            PrimaryForeground = source.PrimaryForeground,
            Secondary = source.Secondary,
            SecondaryForeground = source.SecondaryForeground,
            Background = source.Background,
            Foreground = source.Foreground,
            Destructive = source.Destructive,
            DestructiveForeground = source.DestructiveForeground,
            Success = source.Success,
            SuccessForeground = source.SuccessForeground,
            Warning = source.Warning,
            WarningForeground = source.WarningForeground,
            Info = source.Info,
            InfoForeground = source.InfoForeground,
            Border = source.Border,
            Input = source.Input,
            Ring = source.Ring,
            Muted = source.Muted,
            MutedForeground = source.MutedForeground,
            Accent = source.Accent,
            AccentForeground = source.AccentForeground,
            Card = source.Card,
            CardForeground = source.CardForeground,
            Popover = source.Popover,
            PopoverForeground = source.PopoverForeground
        };
    }
}
