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
using Ivy.Widgets.Internal;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class VaultThemesDialog(
    IState<bool> dialogOpen,
    IVaultService vaultService,
    IClientProvider client,
    IConfigService config,
    string? vaultId,
    Action onThemesUpdated,
    IState<string>? requestedTab = null,
    IState<VaultThemeManifest?>? themeToEdit = null,
    IThemeSerializationService? themeSerializationService = null) : ViewBase
{
    private readonly IThemeSerializationService _themeSerializer = themeSerializationService ?? ThemeSerializationService.Default;

    public override object? Build()
    {
        var selectedTabIndex = UseState(() => requestedTab?.Value == "themes" ? 1 : 0);
        var isSaving = UseState(false);
        var isDeleting = UseState<string?>(null);
        var isExportOpen = UseState(false);
        var isImportOpen = UseState(false);
        var importCodeState = UseState("");
        var importErrorState = UseState<string?>(null);

        // Mock interactive component preview states
        var mockInputText = UseState("Ivy Tendril");
        var mockSelectValue = UseState("main");
        var mockSwitchValue = UseState(true);

        // Initial theme tracking so closing reverts changes unless saved
        var initialThemeId = UseState(() => config.Settings.Theme);
        var initialThemeMode = UseState(() => config.Settings.ThemeMode);
        var initialized = UseState(false);

        var themesQuery = UseQuery<List<VaultThemeManifest>, string>(
            $"vault_themes_{vaultId}",
            async (_, _) => await vaultService.GetThemesAsync(vaultId));

        // Generator states
        var themeName = UseState("Team Brand");
        var themeDesc = UseState("Custom team vault theme");
        var editingThemeManifestId = UseState<string?>(null);
        var selectedPreset = UseState("Default");
        var selectedMode = UseState("light"); // "light" or "dark"

        var fontFamilyState = UseState("Geist");
        var fontSizeState = UseState("16px");

        var editingTheme = UseState(() => CloneTheme(TendrilThemes.Default.IvyTheme));

        // Real-time CSS theme synchronization to client
        UseEffect(() =>
        {
            var themeService = new ThemeService();
            themeService.SetTheme(editingTheme.Value);
            var css = themeService.GenerateThemeCss();
            client.ApplyTheme(css);
        }, editingTheme);

        // Synchronize client theme mode on mount
        if (!initialized.Value)
        {
            initialized.Set(true);
            var mode = selectedMode.Value == "dark" ? ThemeMode.Dark : ThemeMode.Light;
            client.SetThemeMode(mode);
        }

        var presets = new Dictionary<string, Theme>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = TendrilThemes.CreateDefaultIvyTheme(),
            ["Ocean"] = GetOceanTheme(),
            ["Forest"] = GetForestTheme(),
            ["Sunset"] = GetSunsetTheme(),
            ["Midnight"] = GetMidnightTheme()
        };

        foreach (var t in TendrilThemes.BuiltInThemes)
        {
            if (!presets.ContainsKey(t.Name))
            {
                presets[t.Name] = t.IvyTheme;
            }
        }

        void LoadPreset(Theme preset)
        {
            editingTheme.Set(CloneTheme(preset));
            if (!string.IsNullOrEmpty(preset.FontFamily))
                fontFamilyState.Set(preset.FontFamily);
            if (!string.IsNullOrEmpty(preset.FontSize))
                fontSizeState.Set(preset.FontSize);
            client.Toast($"Loaded {preset.Name} theme", "Theme");
        }

        UseEffect(() =>
        {
            if (presets.TryGetValue(selectedPreset.Value, out var preset))
            {
                LoadPreset(preset);
            }
        }, selectedPreset);

        UseEffect(() =>
        {
            var newTheme = CloneTheme(editingTheme.Value);
            newTheme.FontFamily = string.IsNullOrWhiteSpace(fontFamilyState.Value) ? null : fontFamilyState.Value;
            editingTheme.Set(newTheme);
        }, fontFamilyState);

        UseEffect(() =>
        {
            var newTheme = CloneTheme(editingTheme.Value);
            newTheme.FontSize = string.IsNullOrWhiteSpace(fontSizeState.Value) ? null : fontSizeState.Value;
            editingTheme.Set(newTheme);
        }, fontSizeState);

        UseEffect(() =>
        {
            if (requestedTab != null && !string.IsNullOrEmpty(requestedTab.Value))
            {
                selectedTabIndex.Set(requestedTab.Value == "themes" ? 1 : 0);
            }
        }, requestedTab);

        UseEffect(() =>
        {
            if (themeToEdit?.Value != null)
            {
                var t = themeToEdit.Value;
                themeName.Set(t.Name);
                themeDesc.Set(t.Description);
                editingThemeManifestId.Set(t.Id);
                if (t.IvyTheme != null)
                {
                    editingTheme.Set(CloneTheme(t.IvyTheme));
                    if (!string.IsNullOrEmpty(t.IvyTheme.FontFamily))
                        fontFamilyState.Set(t.IvyTheme.FontFamily);
                    if (!string.IsNullOrEmpty(t.IvyTheme.FontSize))
                        fontSizeState.Set(t.IvyTheme.FontSize);
                }
                selectedMode.Set(t.IsDark ? "dark" : "light");
                selectedTabIndex.Set(0);
                themeToEdit.Set(null);
            }
        }, themeToEdit);

        if (!dialogOpen.Value) return null;

        var themesList = themesQuery.Value ?? new List<VaultThemeManifest>();

        void HandleClose()
        {
            TendrilThemes.ApplyTheme(client, initialThemeId.Value);
            TendrilThemes.ApplyThemeMode(client, initialThemeMode.Value);
            dialogOpen.Set(false);
        }

        void UpdateColor(Action<ThemeColors> updater)
        {
            var newTheme = CloneTheme(editingTheme.Value);
            var colors = selectedMode.Value == "light" ? newTheme.Colors.Light : newTheme.Colors.Dark;
            updater(colors);
            editingTheme.Set(newTheme);
        }

        void UpdateThemeProperty(Action<Theme> updater)
        {
            var newTheme = CloneTheme(editingTheme.Value);
            updater(newTheme);
            editingTheme.Set(newTheme);
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

            var id = editingThemeManifestId.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Regex.Replace(rawName.ToLowerInvariant(), @"[^a-z0-9_-]", "-").Trim('-');
                if (string.IsNullOrWhiteSpace(id))
                {
                    id = "vault-theme-" + Guid.NewGuid().ToString("N")[..6];
                }
            }

            var curColors = selectedMode.Value == "light"
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
                    curColors.Primary ?? "#18181b",
                    curColors.Secondary ?? "#71717a",
                    curColors.Accent ?? "#27272a",
                    curColors.Background ?? "#ffffff"
                ],
                IvyTheme = CloneTheme(editingTheme.Value)
            };

            manifest.IvyTheme.Name = manifest.Name;
            manifest.IvyTheme.FontFamily = string.IsNullOrWhiteSpace(fontFamilyState.Value) ? null : fontFamilyState.Value;
            manifest.IvyTheme.FontSize = string.IsNullOrWhiteSpace(fontSizeState.Value) ? null : fontSizeState.Value;

            var result = await vaultService.SaveThemeToVaultAsync(manifest, vaultId);
            isSaving.Set(false);

            if (result.Success)
            {
                client.Toast(result.Message, "Vault Theme Saved");
                TendrilThemes.ApplyTheme(client, manifest.Id);
                config.Settings.Theme = manifest.Id;
                config.SaveSettings();
                initialThemeId.Set(manifest.Id);

                themesQuery.Mutator.Revalidate();
                onThemesUpdated();
                if (themesList.Count > 0)
                {
                    selectedTabIndex.Set(1);
                }
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
            editingThemeManifestId.Set(t.Id);
            if (t.IvyTheme != null)
            {
                editingTheme.Set(CloneTheme(t.IvyTheme));
                if (!string.IsNullOrEmpty(t.IvyTheme.FontFamily))
                    fontFamilyState.Set(t.IvyTheme.FontFamily);
                if (!string.IsNullOrEmpty(t.IvyTheme.FontSize))
                    fontSizeState.Set(t.IvyTheme.FontSize);
            }
            selectedMode.Set(t.IsDark ? "dark" : "light");
            selectedTabIndex.Set(0);
        }

        void HandleImport(string raw)
        {
            if (_themeSerializer.TryImportTheme(raw, out var imported, out var err))
            {
                editingTheme.Set(CloneTheme(imported));
                if (!string.IsNullOrWhiteSpace(imported.Name))
                {
                    themeName.Set(imported.Name);
                }
                if (!string.IsNullOrWhiteSpace(imported.FontFamily))
                {
                    fontFamilyState.Set(imported.FontFamily);
                }
                if (!string.IsNullOrWhiteSpace(imported.FontSize))
                {
                    fontSizeState.Set(imported.FontSize);
                }
                importErrorState.Set(null);
                isImportOpen.Set(false);
                client.Toast($"Imported theme configuration '{(string.IsNullOrWhiteSpace(imported.Name) ? "Custom" : imported.Name)}'", "Theme Imported");
            }
            else
            {
                importErrorState.Set(err ?? "Invalid theme format.");
            }
        }

        // ==========================================
        // TAB 1: Existing Vault Themes
        // ==========================================
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
                | Text.P("This team vault doesn't have any custom themes yet. Use the Theme Generator to create and upload one.").Small().Muted()
                | new Button("Create Custom Theme")
                    .Icon(Icons.Plus)
                    .Primary()
                    .OnClick(() =>
                    {
                        editingThemeManifestId.Set(null);
                        selectedTabIndex.Set(0);
                    });
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
                                initialThemeId.Set(t.Id);
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
                        .OnClick(() =>
                        {
                            editingThemeManifestId.Set(null);
                            themeName.Set("Team Brand");
                            themeDesc.Set("Custom team vault theme");
                            selectedTabIndex.Set(0);
                        }))
                | new Separator()
                | rows;
        }

        // ==========================================
        // TAB 2: Theme Generator (Matching ThemeCustomizer from Ivy Samples)
        // ==========================================
        var currentColors = selectedMode.Value == "light"
            ? editingTheme.Value.Colors.Light
            : editingTheme.Value.Colors.Dark;

        var presetOptions = presets.Select(kv => new Option<string>(kv.Key, kv.Key)).ToArray();

        // Live Swatches & Preview Row
        var livePreviewSwatches = Layout.Horizontal()
            | new[] { currentColors.Primary, currentColors.Secondary, currentColors.Accent, currentColors.Background, currentColors.Foreground }
                .Select(c =>
                    new Svg($"<svg width='20' height='20' viewBox='0 0 20 20'><circle cx='10' cy='10' r='9' fill='{c ?? "#888888"}' stroke='rgba(128,128,128,0.3)' stroke-width='1.5'/></svg>")
                        .Width(Size.Px(20))
                        .Height(Size.Px(20))
                ).ToArray();

        var mockComponentPreview = new Card(
            Layout.Vertical().AlignContent(Align.Left)
                | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | (Layout.Vertical().AlignContent(Align.Left)
                        | Text.Block("Interactive Preview").Bold().Small()
                        | Text.P("Live preview responding to colors, typography, and border-radius").Small().Muted())
                    | (selectedMode.Value == "dark"
                        ? new Badge("Dark Mode").Variant(BadgeVariant.Secondary).Small()
                        : new Badge("Light Mode").Variant(BadgeVariant.Secondary).Small()))
                | new Separator()
                | Text.Block("Buttons").Small().Bold()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Button("Primary").Primary().Small().Icon(Icons.Sparkles)
                    | new Button("Secondary").Secondary().Small()
                    | new Button("Outline").Outline().Small()
                    | new Button("Destructive").Destructive().Small().Icon(Icons.Trash2)
                    | new Button("Ghost").Ghost().Small())
                | Text.Block("Badges").Small().Bold()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | new Badge("Primary").Variant(BadgeVariant.Primary).Small()
                    | new Badge("Secondary").Variant(BadgeVariant.Secondary).Small()
                    | new Badge("Outline").Variant(BadgeVariant.Outline).Small()
                    | new Badge("Destructive").Variant(BadgeVariant.Destructive).Small()
                    | new Badge("Active (Success)").Variant(BadgeVariant.Info).Small())
                | Text.Block("Form Fields & Selectors").Small().Bold()
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | mockInputText.ToTextInput().Placeholder("Text input...").WithField().Label("Text Field").Width(Size.Units(65))
                    | mockSelectValue.ToSelectInput(options: [
                        new Option<string>("main", "main branch"),
                        new Option<string>("dev", "dev branch"),
                        new Option<string>("staging", "staging branch")
                      ]).WithField().Label("Dropdown").Width(Size.Units(55))
                    | (Layout.Vertical().AlignContent(Align.Left).Width(Size.Units(45))
                        | Text.Block("Toggle Switch").Small()
                        | mockSwitchValue.ToSwitchInput()))
                | (Layout.Horizontal().AlignContent(Align.Left)
                    | Callout.Info("Boxes, fields, and selectors inherit your border-radius and color variables.").Width(Size.Fraction(0.6f))
                    | (Layout.Vertical().AlignContent(Align.Left).Width(Size.Fraction(0.4f))
                        | new Card(
                            Layout.Vertical().AlignContent(Align.Left)
                                | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                                    | Text.Block("Sample Metric").Small().Muted()
                                    | new Icon(Icons.Activity))
                                | Text.Block("99.9%").Bold()
                                | new Badge("Healthy").Variant(BadgeVariant.Secondary).Small()
                        )))
        );

        // Export code dialog
        var exportDialog = isExportOpen.Value
            ? new Dialog(
                _ => isExportOpen.Set(false),
                new DialogHeader("Export Theme Configuration"),
                new DialogBody(
                    Layout.Tabs(
                        new Tab(
                            "C#",
                            Layout.Vertical()
                                | Text.P("Copy this C# configuration into your server setup.").Small()
                                | new CodeBlock(_themeSerializer.ExportToCSharp(editingTheme.Value), Languages.Csharp)
                                | new Button("Copy C# Code")
                                    .Primary()
                                    .Icon(Icons.ClipboardCopy, Align.Right)
                                    .OnClick(() =>
                                    {
                                        client.CopyToClipboard(_themeSerializer.ExportToCSharp(editingTheme.Value));
                                        client.Toast("C# theme configuration copied to clipboard!", "Export");
                                    })
                        ).Icon(Icons.Code),
                        new Tab(
                            "JSON",
                            Layout.Vertical()
                                | Text.P("Use this JSON to persist or share the theme.").Small()
                                | new CodeBlock(_themeSerializer.ExportToJson(editingTheme.Value),
                                    Languages.Json)
                                | new Button("Copy JSON")
                                    .Primary()
                                    .Icon(Icons.ClipboardCopy, Align.Right)
                                    .OnClick(() =>
                                    {
                                        var json = _themeSerializer.ExportToJson(editingTheme.Value);
                                        client.CopyToClipboard(json);
                                        client.Toast("JSON theme configuration copied to clipboard!", "Export");
                                    })
                        ).Icon(Icons.FileBraces)
                    )
                ),
                new DialogFooter(
                    new Button("Close", _ => isExportOpen.Set(false), variant: ButtonVariant.Secondary)
                )
            ).Width(Size.Units(160))
            : null;

        // Import code dialog
        var importDialog = isImportOpen.Value
            ? new Dialog(
                _ => isImportOpen.Set(false),
                new DialogHeader("Import Theme Configuration"),
                new DialogBody(
                    Layout.Vertical().AlignContent(Align.Left)
                        | Text.P("Paste a JSON or C# theme configuration below to import its colors, typography, and border radius settings.").Small().Muted()
                        | (importErrorState.Value != null ? Callout.Destructive(importErrorState.Value) : null!)
                        | importCodeState.ToTextareaInput().Placeholder("{\n  \"Name\": \"Ocean\",\n  \"Colors\": {\n    \"Light\": { ... },\n    \"Dark\": { ... }\n  }\n}").Height(Size.Units(55))
                ),
                new DialogFooter(
                    Layout.Horizontal().AlignContent(Align.Right)
                        | new Button("Cancel").Outline().OnClick(() => isImportOpen.Set(false))
                        | new Button("Import Theme")
                            .Primary()
                            .Icon(Icons.FileDown)
                            .Disabled(string.IsNullOrWhiteSpace(importCodeState.Value))
                            .OnClick(() => HandleImport(importCodeState.Value))
                )
            ).Width(Size.Units(160))
            : null;

        var generatorTabContent = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | themeName.ToTextInput("Theme Name").WithField().Label("Theme Name").Width(Size.Units(70))
                | themeDesc.ToTextInput("Description").WithField().Label("Description").Width(Size.Units(110)))
            | (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                | (Layout.Vertical().AlignContent(Align.Left).Width(Size.Units(90))
                    | Text.H3("Theme Preset").Small()
                    | selectedPreset.ToSelectInput(options: presetOptions))
                | (Layout.Vertical().AlignContent(Align.Left).Width(Size.Units(90))
                    | Text.H3("Theme Mode").Small()
                    | (Layout.Horizontal()
                        | new Button("Light")
                            .Variant(selectedMode.Value == "light" ? ButtonVariant.Primary : ButtonVariant.Outline)
                            .Icon(Icons.Sun)
                            .OnClick(() =>
                            {
                                selectedMode.Set("light");
                                client.SetThemeMode(ThemeMode.Light);
                            })
                            .Width(Size.Full())
                        | new Button("Dark")
                            .Variant(selectedMode.Value == "dark" ? ButtonVariant.Primary : ButtonVariant.Outline)
                            .Icon(Icons.Moon)
                            .OnClick(() =>
                            {
                                selectedMode.Set("dark");
                                client.SetThemeMode(ThemeMode.Dark);
                            })
                            .Width(Size.Full()))))
            | new Separator()
            | new Expandable(
                header: (Layout.Horizontal().AlignContent(Align.SpaceBetween)
                    | Text.Block("Mock Components Preview").Bold()
                    | livePreviewSwatches),
                content: mockComponentPreview
            ).Height(Size.Fit()).Open()
            | new Separator()
            | new Expandable(
                header: Text.Block("Colors").Bold(),
                content: Layout.Vertical()
                    | Text.Block("Main Colors").Small()
                    | (Layout.Grid().Columns(4)
                        | new ThemeColorPicker(currentColors.Primary ?? "#000000", e => UpdateColor(c => c.Primary = e.Value), placeholder: "Primary").AllowAlpha().WithField().Medium().Description("Primary").WithTooltip($"Primary: {currentColors.Primary ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.PrimaryForeground ?? "#000000", e => UpdateColor(c => c.PrimaryForeground = e.Value), placeholder: "Primary Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Primary Foreground: {currentColors.PrimaryForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Secondary ?? "#000000", e => UpdateColor(c => c.Secondary = e.Value), placeholder: "Secondary").AllowAlpha().WithField().Medium().Description("Secondary").WithTooltip($"Secondary: {currentColors.Secondary ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.SecondaryForeground ?? "#000000", e => UpdateColor(c => c.SecondaryForeground = e.Value), placeholder: "Secondary Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Secondary Foreground: {currentColors.SecondaryForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Background ?? "#000000", e => UpdateColor(c => c.Background = e.Value), placeholder: "Background").AllowAlpha().WithField().Medium().Description("Background").WithTooltip($"Background: {currentColors.Background ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Foreground ?? "#000000", e => UpdateColor(c => c.Foreground = e.Value), placeholder: "Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Foreground: {currentColors.Foreground ?? "#000000"}"))
                    | new Separator()
                    | Text.Block("Semantic Colors").Small()
                    | (Layout.Grid().Columns(4)
                        | new ThemeColorPicker(currentColors.Success ?? "#000000", e => UpdateColor(c => c.Success = e.Value), placeholder: "Success").AllowAlpha().WithField().Medium().Description("Success").WithTooltip($"Success: {currentColors.Success ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.SuccessForeground ?? "#000000", e => UpdateColor(c => c.SuccessForeground = e.Value), placeholder: "Success Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Success Foreground: {currentColors.SuccessForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Destructive ?? "#000000", e => UpdateColor(c => c.Destructive = e.Value), placeholder: "Destructive").AllowAlpha().WithField().Medium().Description("Destructive").WithTooltip($"Destructive: {currentColors.Destructive ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.DestructiveForeground ?? "#000000", e => UpdateColor(c => c.DestructiveForeground = e.Value), placeholder: "Destructive Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Destructive Foreground: {currentColors.DestructiveForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Warning ?? "#000000", e => UpdateColor(c => c.Warning = e.Value), placeholder: "Warning").AllowAlpha().WithField().Medium().Description("Warning").WithTooltip($"Warning: {currentColors.Warning ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.WarningForeground ?? "#000000", e => UpdateColor(c => c.WarningForeground = e.Value), placeholder: "Warning Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Warning Foreground: {currentColors.WarningForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Info ?? "#000000", e => UpdateColor(c => c.Info = e.Value), placeholder: "Info").AllowAlpha().WithField().Medium().Description("Info").WithTooltip($"Info: {currentColors.Info ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.InfoForeground ?? "#000000", e => UpdateColor(c => c.InfoForeground = e.Value), placeholder: "Info Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Info Foreground: {currentColors.InfoForeground ?? "#000000"}"))
                    | new Separator()
                    | Text.Block("UI Element Colors").Small()
                    | (Layout.Grid().Columns(4)
                        | new ThemeColorPicker(currentColors.Muted ?? "#000000", e => UpdateColor(c => c.Muted = e.Value), placeholder: "Muted").AllowAlpha().WithField().Medium().Description("Muted").WithTooltip($"Muted: {currentColors.Muted ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.MutedForeground ?? "#000000", e => UpdateColor(c => c.MutedForeground = e.Value), placeholder: "Muted Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Muted Foreground: {currentColors.MutedForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Accent ?? "#000000", e => UpdateColor(c => c.Accent = e.Value), placeholder: "Accent").AllowAlpha().WithField().Medium().Description("Accent").WithTooltip($"Accent: {currentColors.Accent ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.AccentForeground ?? "#000000", e => UpdateColor(c => c.AccentForeground = e.Value), placeholder: "Accent Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Accent Foreground: {currentColors.AccentForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Card ?? "#000000", e => UpdateColor(c => c.Card = e.Value), placeholder: "Card").AllowAlpha().WithField().Medium().Description("Card").WithTooltip($"Card: {currentColors.Card ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.CardForeground ?? "#000000", e => UpdateColor(c => c.CardForeground = e.Value), placeholder: "Card Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Card Foreground: {currentColors.CardForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Popover ?? "#000000", e => UpdateColor(c => c.Popover = e.Value), placeholder: "Popover").AllowAlpha().WithField().Medium().Description("Popover").WithTooltip($"Popover: {currentColors.Popover ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.PopoverForeground ?? "#000000", e => UpdateColor(c => c.PopoverForeground = e.Value), placeholder: "Popover Foreground").Foreground(true).AllowAlpha().WithField().Medium().Description("\u00A0").WithTooltip($"Popover Foreground: {currentColors.PopoverForeground ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Border ?? "#000000", e => UpdateColor(c => c.Border = e.Value), placeholder: "Border").AllowAlpha().WithField().Medium().Description("Border").WithTooltip($"Border: {currentColors.Border ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Input ?? "#000000", e => UpdateColor(c => c.Input = e.Value), placeholder: "Input").AllowAlpha().WithField().Medium().Description("Input").WithTooltip($"Input: {currentColors.Input ?? "#000000"}")
                        | new ThemeColorPicker(currentColors.Ring ?? "#000000", e => UpdateColor(c => c.Ring = e.Value), placeholder: "Ring").AllowAlpha().WithField().Medium().Description("Ring").WithTooltip($"Ring: {currentColors.Ring ?? "#000000"}"))
            ).Height(Size.Fit()).Open()
            | new Expandable(
                "Typography & Layout",
                Layout.Vertical()
                    | fontFamilyState.ToTextInput()
                        .Placeholder("e.g., Inter, system-ui, sans-serif")
                        .WithField().Label("Font Family")
                    | fontSizeState.ToTextInput()
                        .Placeholder("e.g., 16px, 1rem")
                        .WithField().Label("Font Size")
                    | new Separator()
                    | new BorderRadiusSelector(editingTheme, UpdateThemeProperty)
            )
            | new Separator()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | new Button("Copy Configuration")
                    .Outline()
                    .Icon(Icons.Copy)
                    .OnClick(() => isExportOpen.Set(true))
                    .Width(Size.Fraction(0.5f))
                | new Button("Import Configuration")
                    .Outline()
                    .Icon(Icons.FileDown)
                    .OnClick(() =>
                    {
                        importCodeState.Set("");
                        importErrorState.Set(null);
                        isImportOpen.Set(true);
                    })
                    .Width(Size.Fraction(0.5f)))
            | exportDialog
            | importDialog;

        object dialogBody;
        string dialogTitle;

        if (themesList.Count == 0)
        {
            dialogTitle = editingThemeManifestId.Value != null ? "Edit Custom Theme" : "Create Custom Theme";
            dialogBody = generatorTabContent;
        }
        else
        {
            dialogTitle = "Team Vault Themes";
            dialogBody = Layout.Tabs(
                new Tab("Theme Generator", generatorTabContent).Icon(Icons.SlidersHorizontal),
                new Tab($"Vault Themes ({themesList.Count})", themesTabContent).Icon(Icons.Palette)
            )
            .SelectedIndex(selectedTabIndex.Value)
            .OnSelect(i => selectedTabIndex.Set(i));
        }

        var isGeneratorActive = themesList.Count == 0 || selectedTabIndex.Value == 0;

        var dialogActions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Close").Outline().OnClick(HandleClose)
            | (isGeneratorActive
                ? new Button("Upload to Team Vault")
                    .Icon(Icons.Upload)
                    .Primary()
                    .Loading(isSaving.Value)
                    .Disabled(isSaving.Value || string.IsNullOrWhiteSpace(themeName.Value))
                    .OnClick(async () => await HandleSaveToVault())
                : new Button("New Custom Theme")
                    .Icon(Icons.Plus)
                    .Primary()
                    .OnClick(() =>
                    {
                        editingThemeManifestId.Set(null);
                        themeName.Set("Team Brand");
                        themeDesc.Set("Custom team vault theme");
                        selectedTabIndex.Set(0);
                    }));

        return new Dialog(
            _ => HandleClose(),
            new DialogHeader(dialogTitle),
            new DialogBody(dialogBody),
            new DialogFooter(dialogActions)
        ).Width(Size.Units(190));
    }

    // ==========================================
    // Border Radius Selector (from ThemeCustomizer)
    // ==========================================
    private class BorderRadiusSelector(IState<Theme> editingTheme, Action<Action<Theme>> updateThemeProperty) : ViewBase
    {
        private const int PreviewSize = 35;
        private const int CardSize = 60;
        private const int SvgViewBox = 32;

        private static readonly (string Value, int Pixels)[] RadiusOptions =
        [
            ("0px", 0),
            ("0.5rem", 8),
            ("1rem", 16),
            ("1.5rem", 24),
            ("2rem", 32)
        ];

        public override object Build()
        {
            return Layout.Vertical()
                | Text.Block("Radius").Small().Bold()
                | BuildRadiusCategory(
                    "Boxes",
                    "card, modal, alert",
                    editingTheme.Value.BorderRadiusBoxes,
                    value => updateThemeProperty(t => t.BorderRadiusBoxes = value))
                | BuildRadiusCategory(
                    "Fields",
                    "button, input, select, tab",
                    editingTheme.Value.BorderRadiusFields,
                    value => updateThemeProperty(t => t.BorderRadiusFields = value))
                | BuildRadiusCategory(
                    "Selectors",
                    "checkbox, toggle, badge",
                    editingTheme.Value.BorderRadiusSelectors,
                    value => updateThemeProperty(t => t.BorderRadiusSelectors = value));
        }

        private static object BuildRadiusCategory(
            string title,
            string subtitle,
            string? currentValue,
            Action<string?> onUpdate)
        {
            var options = Layout.Horizontal();
            foreach (var (value, pixels) in RadiusOptions)
            {
                options = options | CreateOption(value, pixels, currentValue, onUpdate);
            }

            return Layout.Vertical()
                | Text.Block(title).Bold().Small()
                | Text.Block(subtitle).Muted().Italic()
                | options;
        }

        private static object CreateOption(
            string remValue,
            int pxRadius,
            string? currentValue,
            Action<string?> onUpdate)
        {
            var normalizedCurrent = string.IsNullOrWhiteSpace(currentValue) ? "0px" : currentValue;
            var normalizedOption = remValue == "0px" ? "0px" : remValue;
            var isSelected = normalizedCurrent == normalizedOption;
            var strokeColor = isSelected ? "var(--primary)" : "var(--muted-foreground)";

            var rectSize = SvgViewBox * 2;
            var svgContent = $"<svg width='{PreviewSize}' height='{PreviewSize}' viewBox='0 0 {SvgViewBox} {SvgViewBox}'><rect x='1' y='1' width='{rectSize}' height='{rectSize}' rx='{pxRadius}' fill='none' stroke='{strokeColor}' stroke-width='3'/></svg>";

            return new Card(
                    new Svg(svgContent)
                        .Width(Size.Px(PreviewSize))
                        .Height(Size.Px(PreviewSize))
                )
                .Width(Size.Px(CardSize))
                .Height(Size.Px(CardSize))
                .OnClick(() => onUpdate(remValue == "0px" ? null : remValue))
                .WithTooltip($"{remValue} ({pxRadius}px)");
        }
    }

    // ==========================================
    // Helpers & Presets (from ThemeCustomizer)
    // ==========================================
    private static Theme GetOceanTheme() => new()
    {
        Name = "Ocean",
        FontFamily = "Geist",
        FontSize = "16px",
        BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
        BorderRadiusFields = Theme.Default.BorderRadiusFields,
        BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
        Colors = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = "#0077BE",
                PrimaryForeground = "#FFFFFF",
                Secondary = "#5B9BD5",
                SecondaryForeground = "#FFFFFF",
                Background = "#F0F8FF",
                Foreground = "#1A1A1A",
                Destructive = "#DC143C",
                DestructiveForeground = "#FFFFFF",
                Success = "#20B2AA",
                SuccessForeground = "#FFFFFF",
                Warning = "#FFD700",
                WarningForeground = "#1A1A1A",
                Info = "#4682B4",
                InfoForeground = "#FFFFFF",
                Border = "#B0C4DE",
                Input = "#E6F2FF",
                Ring = "#0077BE",
                Muted = "#E0E8F0",
                MutedForeground = "#5A6A7A",
                Accent = "#87CEEB",
                AccentForeground = "#1A1A1A",
                Card = "#FFFFFF",
                CardForeground = "#1A1A1A",
                Popover = "#F0F8FF",
                PopoverForeground = "#1A1A1A"
            },
            Dark = new ThemeColors
            {
                Primary = "#4A9EFF",
                PrimaryForeground = "#001122",
                Secondary = "#2D4F70",
                SecondaryForeground = "#E8F4FD",
                Background = "#001122",
                Foreground = "#E8F4FD",
                Destructive = "#FF6B7D",
                DestructiveForeground = "#FFFFFF",
                Success = "#4ECDC4",
                SuccessForeground = "#001122",
                Warning = "#FFE066",
                WarningForeground = "#001122",
                Info = "#87CEEB",
                InfoForeground = "#001122",
                Border = "#1A3A5C",
                Input = "#0F2A4A",
                Ring = "#4A9EFF",
                Muted = "#0F2A4A",
                MutedForeground = "#8BB3D9",
                Accent = "#1A3A5C",
                AccentForeground = "#E8F4FD",
                Card = "#0F2A4A",
                CardForeground = "#E8F4FD",
                Popover = "#001122",
                PopoverForeground = "#E8F4FD"
            }
        }
    };

    private static Theme GetForestTheme() => new()
    {
        Name = "Forest",
        FontFamily = "Geist",
        FontSize = "16px",
        BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
        BorderRadiusFields = Theme.Default.BorderRadiusFields,
        BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
        Colors = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = "#228B22",
                PrimaryForeground = "#FFFFFF",
                Secondary = "#8FBC8F",
                SecondaryForeground = "#1A1A1A",
                Background = "#F0FFF0",
                Foreground = "#1A1A1A",
                Destructive = "#B22222",
                DestructiveForeground = "#FFFFFF",
                Success = "#32CD32",
                SuccessForeground = "#FFFFFF",
                Warning = "#FFA500",
                WarningForeground = "#1A1A1A",
                Info = "#4169E1",
                InfoForeground = "#FFFFFF",
                Border = "#90EE90",
                Input = "#E8F5E8",
                Ring = "#228B22",
                Muted = "#E0F0E0",
                MutedForeground = "#4A5A4A",
                Accent = "#98FB98",
                AccentForeground = "#1A1A1A",
                Card = "#FFFFFF",
                CardForeground = "#1A1A1A",
                Popover = "#F0FFF0",
                PopoverForeground = "#1A1A1A"
            },
            Dark = new ThemeColors
            {
                Primary = "#4AFF4A",
                PrimaryForeground = "#001100",
                Secondary = "#2D4A2D",
                SecondaryForeground = "#E8FFE8",
                Background = "#001100",
                Foreground = "#E8FFE8",
                Destructive = "#FF4444",
                DestructiveForeground = "#FFFFFF",
                Success = "#66FF66",
                SuccessForeground = "#001100",
                Warning = "#FFB84D",
                WarningForeground = "#001100",
                Info = "#6A9BFF",
                InfoForeground = "#001100",
                Border = "#1A3A1A",
                Input = "#0F2A0F",
                Ring = "#4AFF4A",
                Muted = "#0F2A0F",
                MutedForeground = "#8BC98B",
                Accent = "#1A3A1A",
                AccentForeground = "#E8FFE8",
                Card = "#0F2A0F",
                CardForeground = "#E8FFE8",
                Popover = "#001100",
                PopoverForeground = "#E8FFE8"
            }
        }
    };

    private static Theme GetSunsetTheme() => new()
    {
        Name = "Sunset",
        FontFamily = "Geist",
        FontSize = "16px",
        BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
        BorderRadiusFields = Theme.Default.BorderRadiusFields,
        BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
        Colors = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = "#FF6347",
                PrimaryForeground = "#FFFFFF",
                Secondary = "#FFB6C1",
                SecondaryForeground = "#1A1A1A",
                Background = "#FFF5EE",
                Foreground = "#1A1A1A",
                Destructive = "#DC143C",
                DestructiveForeground = "#FFFFFF",
                Success = "#90EE90",
                SuccessForeground = "#1A1A1A",
                Warning = "#FFD700",
                WarningForeground = "#1A1A1A",
                Info = "#87CEEB",
                InfoForeground = "#1A1A1A",
                Border = "#FFE4E1",
                Input = "#FFF0E6",
                Ring = "#FF6347",
                Muted = "#FFDAB9",
                MutedForeground = "#8B4513",
                Accent = "#FFA07A",
                AccentForeground = "#1A1A1A",
                Card = "#FFFFFF",
                CardForeground = "#1A1A1A",
                Popover = "#FFF5EE",
                PopoverForeground = "#1A1A1A"
            },
            Dark = new ThemeColors
            {
                Primary = "#FF8A65",
                PrimaryForeground = "#2A1100",
                Secondary = "#8D4A47",
                SecondaryForeground = "#FFE8E1",
                Background = "#2A1100",
                Foreground = "#FFE8E1",
                Destructive = "#FF5252",
                DestructiveForeground = "#FFFFFF",
                Success = "#81C784",
                SuccessForeground = "#2A1100",
                Warning = "#FFB74D",
                WarningForeground = "#2A1100",
                Info = "#64B5F6",
                InfoForeground = "#2A1100",
                Border = "#5D2A1A",
                Input = "#3D1F0F",
                Ring = "#FF8A65",
                Muted = "#3D1F0F",
                MutedForeground = "#C19A8A",
                Accent = "#5D2A1A",
                AccentForeground = "#FFE8E1",
                Card = "#3D1F0F",
                CardForeground = "#FFE8E1",
                Popover = "#2A1100",
                PopoverForeground = "#FFE8E1"
            }
        }
    };

    private static Theme GetMidnightTheme() => new()
    {
        Name = "Midnight",
        FontFamily = "Geist",
        FontSize = "16px",
        BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
        BorderRadiusFields = Theme.Default.BorderRadiusFields,
        BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
        Colors = new ThemeColorScheme
        {
            Light = new ThemeColors
            {
                Primary = "#7C3AED",
                PrimaryForeground = "#FFFFFF",
                Secondary = "#DDD6FE",
                SecondaryForeground = "#1A1A1A",
                Background = "#FAFAFA",
                Foreground = "#1A1A1A",
                Destructive = "#EF4444",
                DestructiveForeground = "#FFFFFF",
                Success = "#10B981",
                SuccessForeground = "#FFFFFF",
                Warning = "#F59E0B",
                WarningForeground = "#000000",
                Info = "#3B82F6",
                InfoForeground = "#FFFFFF",
                Border = "#E5E7EB",
                Input = "#F3F4F6",
                Ring = "#7C3AED",
                Muted = "#F9FAFB",
                MutedForeground = "#6B7280",
                Accent = "#F3F0FF",
                AccentForeground = "#1A1A1A",
                Card = "#FFFFFF",
                CardForeground = "#1A1A1A",
                Popover = "#FAFAFA",
                PopoverForeground = "#1A1A1A"
            },
            Dark = new ThemeColors
            {
                Primary = "#A78BFA",
                PrimaryForeground = "#1A1A2E",
                Secondary = "#4C1D95",
                SecondaryForeground = "#E5E5E5",
                Background = "#0F0F23",
                Foreground = "#E5E5E5",
                Destructive = "#EF4444",
                DestructiveForeground = "#FFFFFF",
                Success = "#10B981",
                SuccessForeground = "#FFFFFF",
                Warning = "#F59E0B",
                WarningForeground = "#000000",
                Info = "#3B82F6",
                InfoForeground = "#FFFFFF",
                Border = "#374151",
                Input = "#1F2937",
                Ring = "#A78BFA",
                Muted = "#1F2937",
                MutedForeground = "#9CA3AF",
                Accent = "#6366F1",
                AccentForeground = "#FFFFFF",
                Card = "#1A1A2E",
                CardForeground = "#E5E5E5",
                Popover = "#0F0F23",
                PopoverForeground = "#E5E5E5"
            }
        }
    };

    private static Theme CloneTheme(Theme source) => TendrilThemes.CloneTheme(source);

    private static ThemeColors CloneThemeColors(ThemeColors source) => TendrilThemes.CloneThemeColors(source);

    [Obsolete("Use IThemeSerializationService or ThemeSerializationService.Default instead.")]
    public static bool TryImportTheme(string raw, out Theme importedTheme, out string? errorMessage)
        => ThemeSerializationService.Default.TryImportTheme(raw, out importedTheme, out errorMessage);
}
