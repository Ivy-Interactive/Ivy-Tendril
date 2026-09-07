using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    IState<VaultThemeManifest?>? themeToEdit = null) : ViewBase
{
    public override object? Build()
    {
        var activeTab = UseState(() => requestedTab?.Value ?? "themes");
        var isSaving = UseState(false);
        var isDeleting = UseState<string?>(null);
        var isExportOpen = UseState(false);

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
            ["Default"] = Theme.Default,
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
                activeTab.Set(requestedTab.Value);
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
                activeTab.Set("generator");
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
            activeTab.Set("generator");
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
                        activeTab.Set("generator");
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
                            activeTab.Set("generator");
                        }))
                | new Separator()
                | rows;
        }

        // ==========================================
        // TAB 2: Theme Designer (exactly like in Ivy samples ThemeCustomizer)
        // ==========================================
        var currentColors = selectedMode.Value == "light"
            ? editingTheme.Value.Colors.Light
            : editingTheme.Value.Colors.Dark;

        var presetOptions = presets.Select(kv => new Option<string>(kv.Key, kv.Key)).ToArray();

        // Left editor sidebar content
        var editorContent = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | themeName.ToTextInput("Theme Name").WithField().Label("Name").Width(Size.Units(45))
                | themeDesc.ToTextInput("Description").WithField().Label("Description").Width(Size.Units(50)))
            | Text.H3("Theme Preset").Small()
            | selectedPreset.ToSelectInput(options: presetOptions)
            | new Separator()
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
                    .Width(Size.Full()))
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
            | new Button("Copy Configuration")
                .Outline()
                .Icon(Icons.Copy)
                .OnClick(() => isExportOpen.Set(true))
                .Width(Size.Full());

        // Right live preview panel
        var previewPanel = Layout.Vertical().Width(Size.Full())
            | Text.H2("Live Preview")
            | Text.P("See your theme changes in real-time").Small().Muted()
            | Layout.Tabs(
                new Tab("Components", new InteractiveThemePreview(editingTheme.Value)).Icon(Icons.LayoutPanelLeft),
                new Tab("Dashboard", new DashboardPreview()).Icon(Icons.LayoutDashboard)
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
                                | new CodeBlock(GenerateCSharpCode(editingTheme.Value), Languages.Csharp)
                                | new Button("Copy C# Code")
                                    .Primary()
                                    .Icon(Icons.ClipboardCopy, Align.Right)
                                    .OnClick(() =>
                                    {
                                        client.CopyToClipboard(GenerateCSharpCode(editingTheme.Value));
                                        client.Toast("C# theme configuration copied to clipboard!", "Export");
                                    })
                        ).Icon(Icons.Code),
                        new Tab(
                            "JSON",
                            Layout.Vertical()
                                | Text.P("Use this JSON to persist or share the theme.").Small()
                                | new CodeBlock(System.Text.Json.JsonSerializer.Serialize(
                                        editingTheme.Value,
                                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                                    Languages.Json)
                                | new Button("Copy JSON")
                                    .Primary()
                                    .Icon(Icons.ClipboardCopy, Align.Right)
                                    .OnClick(() =>
                                    {
                                        var json = System.Text.Json.JsonSerializer.Serialize(
                                            editingTheme.Value,
                                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
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

        var generatorTabContent = Layout.Vertical()
            | (Layout.Horizontal().AlignContent(Align.Left)
                | Layout.Vertical().Width(Size.Px(380))
                    | editorContent
                | Layout.Vertical().Width(Size.Full())
                    | previewPanel)
            | exportDialog;

        var tabs = Layout.Tabs(
            new Tab("Vault Themes", themesTabContent).Icon(Icons.Palette),
            new Tab("Theme Generator", generatorTabContent).Icon(Icons.SlidersHorizontal)
        );

        var dialogActions = Layout.Horizontal().AlignContent(Align.Right)
            | new Button("Close").Outline().OnClick(HandleClose)
            | new Button("Upload to Team Vault")
                .Icon(Icons.Upload)
                .Primary()
                .Loading(isSaving.Value)
                .Disabled(isSaving.Value || string.IsNullOrWhiteSpace(themeName.Value))
                .OnClick(async () => await HandleSaveToVault());

        return new Dialog(
            _ => HandleClose(),
            new DialogHeader("Team Vault Themes"),
            new DialogBody(tabs),
            new DialogFooter(dialogActions)
        ).Width(Size.Units(350));
    }

    // ==========================================
    // Interactive Components Preview
    // ==========================================
    private class InteractiveThemePreview(Theme theme) : ViewBase
    {
        public override object Build()
        {
            var client = UseService<IClientProvider>();

            var payment = UseState(() => new PaymentModel(
                NameOnCard: "John Doe",
                CardNumber: "1234 5678 9012 3456",
                Cvv: "123",
                Month: "MM",
                Year: "YYYY",
                BillingAddress: "",
                SameAsShipping: true,
                Comments: string.Empty
            ));

            var price = UseState(500);
            var agreeTerms = UseState(true);
            var themeSatisfaction = UseState(4);
            var uxSatisfaction = UseState((int?)null);

            var paginationPage = UseState(1);
            var searchText = UseState("");
            var domain = UseState("ivy.app");
            var email = UseState("");
            var selectedCategory = UseState<string?>("Primary");
            var badgeVariant = UseState(new[] { "Success", "Warning", "Info" });
            var disableButtons = UseState(false);
            var disableInputs = UseState(false);
            var dateTimeState = UseState(DateTime.Now);
            var dateRangeState = UseState(() => (from: DateTime.Today.AddDays(-7), to: DateTime.Today));

            var chatMessages = UseState(ImmutableArray.Create(
                new ChatMessage(ChatSender.Assistant,
                    $"You're previewing the '{theme.Name}' theme. Type a message to see how chat looks in this theme.")
            ));

            UseEffect(() =>
            {
                if (!string.IsNullOrWhiteSpace(payment.Value.NameOnCard) &&
                    !string.IsNullOrWhiteSpace(payment.Value.CardNumber))
                {
                    client.Toast($"Payment form submitted for {payment.Value.NameOnCard}", "Form");
                }
            }, payment);

            const int totalPages = 5;
            var themeIcon = GetThemeIcon(theme.Name);
            var statusVariant = GetStatusVariant(theme.Name);

            ValueTask OnChatSend(Event<Ivy.Chat, string> e)
            {
                var trimmed = e.Value.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    return ValueTask.CompletedTask;
                }

                var withUser = chatMessages.Value.Add(new ChatMessage(ChatSender.User, trimmed));
                var withAssistant = withUser.Add(
                    new ChatMessage(ChatSender.Assistant, $"You said: {trimmed}")
                );
                chatMessages.Set(withAssistant);
                return ValueTask.CompletedTask;
            }

            var paymentForm = payment.ToForm("Submit payment")
                .SubmitBuilder(isLoading => new Button("Submit payment").Loading(isLoading).Disabled(isLoading || disableButtons.Value))
                .Clear()
                .Place(m => m.NameOnCard)
                .Place(m => m.CardNumber)
                .Place(m => m.Cvv)
                .PlaceHorizontal(m => m.Month, m => m.Year)
                .Place(m => m.BillingAddress)
                .Place(m => m.SameAsShipping)
                .Place(m => m.Comments)
                .Label(m => m.NameOnCard, "Name on card")
                .Label(m => m.CardNumber, "Card number")
                .Label(m => m.Cvv, "CVV")
                .Label(m => m.Month, "Month")
                .Label(m => m.Year, "Year")
                .Label(m => m.BillingAddress, "Billing address")
                .Label(m => m.SameAsShipping, "Same as shipping address")
                .Label(m => m.Comments, "Comments")
                .Builder(m => m.NameOnCard, s => s.ToTextInput().Disabled(disableInputs.Value))
                .Builder(m => m.CardNumber, s => s.ToTextInput().Disabled(disableInputs.Value))
                .Builder(m => m.Cvv, s => s.ToPasswordInput().Placeholder("CVV").Disabled(disableInputs.Value))
                .Builder(m => m.Comments, s => s.ToTextareaInput().Placeholder("Add any additional comments").Disabled(disableInputs.Value))
                .Builder(m => m.Month, s => s.ToTextInput().Disabled(disableInputs.Value))
                .Builder(m => m.Year, s => s.ToTextInput().Disabled(disableInputs.Value))
                .Builder(m => m.BillingAddress, s => s.ToTextInput().Disabled(disableInputs.Value))
                .Builder(m => m.SameAsShipping, s => s.ToBoolInput().Disabled(disableInputs.Value))
                .Required(m => m.NameOnCard, m => m.CardNumber, m => m.Cvv);

            QueryResult<Option<string>[]> QueryCategories(IViewContext context, string query)
            {
                var categories = new[] { "Primary", "Secondary", "Outline", "Destructive", "Success", "Warning", "Info" };
                return context.UseQuery<Option<string>[], (string, string)>(
                    key: (nameof(QueryCategories), query),
                    fetcher: ct => Task.FromResult(categories
                        .Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .Select(c => new Option<string>(c))
                        .ToArray()));
            }

            QueryResult<Option<string>?> LookupCategory(IViewContext context, string? category)
            {
                return context.UseQuery<Option<string>?, (string, string?)>(
                    key: (nameof(LookupCategory), category),
                    fetcher: ct => Task.FromResult(category != null ? new Option<string>(category) : null));
            }

            Button CreateLoadingButton(string name, ButtonVariant variant) =>
                new Button(name, variant: variant)
                {
                    OnClick = new(_ =>
                    {
                        client.Toast($"{name} button clicked", "Action");
                        return ValueTask.CompletedTask;
                    })
                }.Width(Size.Full()).Disabled(disableButtons.Value);

            static object GetPaginationContent(int page, int total) =>
                new Card(
                    Layout.Vertical().AlignContent(Align.Center)
                        | Text.Block("Theme insight").Small()
                        | Text.P(page switch
                        {
                            1 => "Discover how primary and accent colors shape the whole experience.",
                            2 => "Badges, borders and subtle shadows adapt instantly to your theme.",
                            3 => "Form controls, switches and sliders stay readable in every palette.",
                            4 => "Try a different theme and see how this card transforms.",
                            _ => "You've reached the end of the tour - tweak settings and explore freely."
                        }).Small()
                ).Height(Size.Fit());

            var firstCol = Layout.Vertical()
                | new Card(Layout.Vertical() | paymentForm).Height(Size.Fit())
                | new Card(Layout.Vertical()
                    | Text.Block("Category Selector").Bold()
                    | Text.P("Select a category to see the corresponding action button.").Small()
                    | selectedCategory.ToAsyncSelectInput(QueryCategories, LookupCategory, placeholder: "Select Category").Disabled(disableInputs.Value)
                    | (selectedCategory.Value switch
                    {
                        "Primary" => CreateLoadingButton("Primary", ButtonVariant.Primary),
                        "Secondary" => CreateLoadingButton("Secondary", ButtonVariant.Secondary),
                        "Outline" => CreateLoadingButton("Outline", ButtonVariant.Outline),
                        "Destructive" => CreateLoadingButton("Destructive", ButtonVariant.Destructive),
                        "Success" => CreateLoadingButton("Success", ButtonVariant.Success),
                        "Warning" => CreateLoadingButton("Warning", ButtonVariant.Warning),
                        "Info" => CreateLoadingButton("Info", ButtonVariant.Info),
                        _ => CreateLoadingButton("Primary", ButtonVariant.Primary)
                    }));

            var secondCol = Layout.Vertical()
                | new Card(
                    Layout.Vertical()
                        | Text.Block("Badge Variant Selector").Bold()
                        | Text.P("Select badge variants to display.").Small()
                        | badgeVariant.ToSelectInput(new[]
                        {
                            new Option<string>("Primary", "Primary"),
                            new Option<string>("Destructive", "Destructive"),
                            new Option<string>("Secondary", "Secondary"),
                            new Option<string>("Outline", "Outline"),
                            new Option<string>("Success", "Success"),
                            new Option<string>("Warning", "Warning"),
                            new Option<string>("Info", "Info")
                        }).Variant(SelectInputVariant.Toggle).Disabled(disableInputs.Value)
                        | Text.Block("Selected badges:").Small()
                        | (Layout.Horizontal().AlignContent(Align.Center)
                            | badgeVariant.Value.Select(variant => variant switch
                            {
                                "Primary" => new Badge("Primary").Primary(),
                                "Destructive" => new Badge("Destructive").Destructive(),
                                "Secondary" => new Badge("Secondary").Secondary(),
                                "Outline" => new Badge("Outline").Outline(),
                                "Success" => new Badge("Success").Success(),
                                "Warning" => new Badge("Warning").Warning(),
                                "Info" => new Badge("Info").Info(),
                                _ => new Badge("Primary").Primary()
                            }).ToArray())).Height(Size.Fit())
                | new Box(
                    Layout.Vertical().AlignContent(Align.Center)
                        | Text.Block("Pagination demo").Bold()
                        | GetPaginationContent(paginationPage.Value, totalPages)
                        | new Pagination(paginationPage.Value, totalPages, e =>
                        {
                            paginationPage.Set(e.Value);
                            return ValueTask.CompletedTask;
                        }).Disabled(disableInputs.Value))
                | new Card(Layout.Vertical()
                    | Text.Block("Buttons & Actions").Bold()
                    | (Layout.Horizontal().Height(Size.Fit())
                        | CreateLoadingButton("Primary", ButtonVariant.Primary).Loading()
                        | CreateLoadingButton("Secondary", ButtonVariant.Secondary).Loading()
                        | CreateLoadingButton("Outline", ButtonVariant.Outline).Loading())
                    | (Layout.Horizontal().Width(Size.Full())
                        | (Layout.Vertical().AlignContent(Align.Left)
                            | themeSatisfaction.ToFeedbackInput().Stars().Disabled(disableInputs.Value))
                        | (Layout.Vertical().AlignContent(Align.Right)
                            | uxSatisfaction.ToFeedbackInput().Thumbs().Disabled(disableInputs.Value)))
                    | new Box(Layout.Horizontal().Height(Size.Fit())
                        | agreeTerms.ToBoolInput().Disabled(disableInputs.Value)
                        | Text.Block("I agree to the terms and conditions"))
                    | new Embed("https://github.com/Ivy-Interactive/Ivy-Framework")
                    | (Layout.Horizontal().Height(Size.Fit())
                        | (Layout.Vertical() | new Box(Layout.Horizontal()
                            | (Layout.Vertical().AlignContent(Align.Left) | Text.Block("Disable all buttons"))
                            | disableButtons.ToSwitchInput()))
                        | (Layout.Vertical() | new Box(Layout.Horizontal()
                            | (Layout.Vertical().AlignContent(Align.Left) | Text.Block("Disable all inputs"))
                            | disableInputs.ToSwitchInput())))
                    | (Layout.Vertical().AlignContent(Align.Center) | new Badge($"{theme.Name} theme active", statusVariant, themeIcon).Primary()));

            var thirdCol = Layout.Vertical()
                | new Card(Layout.Vertical() | new Ivy.Chat(chatMessages.Value.ToArray(), OnChatSend).Height(Size.Px(330))).Height(Size.Fit())
                | new Card(Layout.Vertical()
                    | Text.Block("Fields").Bold()
                    | searchText.ToSearchInput().Placeholder("Search in settings").Disabled(disableInputs.Value)
                    | dateRangeState.ToDateRangeInput()
                        .Disabled(disableInputs.Value)
                        .WithField()
                        .Label($"Date Range ({(dateRangeState.Value.to - dateRangeState.Value.from).Days} days)")
                        .Height(Size.Fit())
                    | dateTimeState.ToDateTimeInput()
                        .Format("dd/MM/yyyy HH:mm:ss")
                        .Disabled(disableInputs.Value)
                        .WithField()
                        .Label("DateTime")
                        .Height(Size.Fit())
                    | domain.ToTextInput().Prefix("https://").Disabled(disableInputs.Value)
                    | email.ToTextInput()
                        .Placeholder("Email (Ctrl+E)")
                        .ShortcutKey("Ctrl+E")
                        .Variant(TextInputVariant.Email)
                        .Disabled(disableInputs.Value)
                    | Text.Block("Price range").Bold()
                    | Text.P($"Estimated monthly budget: ${price.Value}").Small()
                    | price.ToSliderInput().Min(0).Max(2000).Step(50).Disabled(disableInputs.Value));

            return Layout.Grid().Columns(3)
                | firstCol
                | secondCol
                | thirdCol;
        }

        private record PaymentModel(
            string NameOnCard,
            string CardNumber,
            string Cvv,
            string Month,
            string Year,
            string BillingAddress,
            bool SameAsShipping,
            string Comments
        );
    }

    // ==========================================
    // Dashboard Preview (tab 2 of preview panel)
    // ==========================================
    private class DashboardPreview : ViewBase
    {
        public override object Build()
        {
            var trendData = new[]
            {
                new { Month = "Jan", Desktop = 186, Mobile = 100 },
                new { Month = "Feb", Desktop = 305, Mobile = 200 },
                new { Month = "Mar", Desktop = 237, Mobile = 300 },
                new { Month = "Apr", Desktop = 73, Mobile = 400 },
                new { Month = "May", Desktop = 209, Mobile = 30 },
                new { Month = "Jun", Desktop = 214, Mobile = 45 },
            };

            return Layout.Vertical()
                | (Layout.Grid().Columns(4)
                    | new MetricView("Total Sales", Icons.DollarSign, ctx => ctx.UseQuery(key: "metric_sales", fetcher: () => Task.FromResult(new MetricRecord("$84,250", 0.21, 0.21, "$800,000"))))
                    | new MetricView("User Engagement", Icons.Users, ctx => ctx.UseQuery(key: "metric_users", fetcher: () => Task.FromResult(new MetricRecord("1,247", 0.125, 0.75, "1,500 users"))))
                    | new MetricView("Task Progress", Icons.Check, ctx => ctx.UseQuery(key: "metric_progress", fetcher: () => Task.FromResult(new MetricRecord("87%", null, 0.87, "100% completion"))))
                    | new MetricView("System Health", Icons.Activity, ctx => ctx.UseQuery(key: "metric_health", fetcher: () => Task.FromResult(new MetricRecord("99.9%", null, 0.99, "100% uptime")))))
                | (Layout.Grid().Columns(2)
                    | new Card(
                        Layout.Vertical()
                            | Text.Block("Monthly Revenue Trend").Bold()
                            | trendData.ToLineChart(style: LineChartStyles.Dashboard)
                                .Dimension("Month", e => e.Month)
                                .Measure("Total", e => e.Sum(f => f.Desktop + f.Mobile))
                      ).Height(Size.Fraction(1))
                    | new Card(
                        Layout.Vertical()
                            | Text.Block("Monthly Revenue Distribution").Bold()
                            | trendData.ToAreaChart(style: AreaChartStyles.Dashboard)
                                .Dimension("Month", e => e.Month)
                                .Measure("Desktop", e => e.Sum(f => f.Desktop))
                                .Measure("Mobile", e => e.Sum(f => f.Mobile))
                      ).Height(Size.Fraction(1))
                );
        }
    }

    // ==========================================
    // Border Radius Selector
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
    // Helpers & Presets
    // ==========================================
    private static Icons GetThemeIcon(string themeName)
    {
        return themeName.ToLowerInvariant() switch
        {
            "ocean" => Icons.Waves,
            "forest" => Icons.TreePine,
            "sunset" => Icons.Sunset,
            "midnight" => Icons.Moon,
            _ => Icons.Palette
        };
    }

    private static BadgeVariant GetStatusVariant(string themeName)
    {
        return themeName.ToLowerInvariant() switch
        {
            "ocean" => BadgeVariant.Info,
            "forest" => BadgeVariant.Success,
            "sunset" => BadgeVariant.Warning,
            "midnight" => BadgeVariant.Secondary,
            _ => BadgeVariant.Primary
        };
    }

    private static string GenerateCSharpCode(Theme theme)
    {
        var lightColors = theme.Colors.Light;
        var darkColors = theme.Colors.Dark;
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
        }}
        theme.FontFamily = ""{theme.FontFamily}"";
        theme.FontSize = ""{theme.FontSize}"";
        theme.BorderRadiusBoxes = ""{theme.BorderRadiusBoxes}"";
        theme.BorderRadiusFields = ""{theme.BorderRadiusFields}"";
        theme.BorderRadiusSelectors = ""{theme.BorderRadiusSelectors}""; 
    }});";
    }

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
