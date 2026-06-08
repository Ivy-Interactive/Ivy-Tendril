# Ivy Tendril Plugin Developer Guide

## Introduction

Ivy Tendril supports plugins that can extend its functionality. Plugins are .NET class libraries that implement the `IIvyPlugin` interface and are loaded dynamically at runtime. They can contribute UI elements, register messaging channels, add full apps, and more.

**Key concepts:**
- Plugins are discovered via directory scanning or a `plugin-references.yaml` file
- Each plugin has a **manifest** describing its identity and a **configuration schema** for user-provided settings
- Configuration is stored in a YAML file (`plugin-config.yaml`) in the plugins directory
- Plugins are isolated using `AssemblyLoadContext` for safe loading/unloading
- Source plugins (containing `.csproj` + `.cs` files) are automatically built with `dotnet build`
- Hot-reload is supported: changes to plugin DLLs or source files trigger automatic reload

**Architecture layers:**
- `Ivy.Plugin.Abstractions` — base interfaces shared across all Ivy hosts (`IIvyPlugin`, `IIvyPluginContext`)
- `Ivy.Tendril.Plugin.Abstractions` — Tendril-specific interfaces (`ITendrilPluginContext`, `IMessagingChannel`)
- `Ivy.Tendril.Plugin.Extended.Abstractions` — UI contribution interfaces (`ITendrilExtendedPluginContext`)

## Quick Start: Hello World Plugin

### 1. Use the GitHub Template

> **To be implemented...** A GitHub template repository for bootstrapping new Tendril plugins is planned but not yet available.

For now, create a new .NET class library manually:

```bash
mkdir MyPlugin && cd MyPlugin
dotnet new classlib -n Ivy.Tendril.Plugin.MyPlugin --framework net10.0
cd Ivy.Tendril.Plugin.MyPlugin
dotnet add package Ivy.Tendril.Plugin.Abstractions
```

### 2. Implement Your Plugin

Create a single class implementing `IIvyPlugin` and mark the assembly with `[IvyPlugin]`:

```csharp
using Ivy.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.MyPlugin.MyPlugin))]

namespace Ivy.Tendril.Plugin.MyPlugin;

public class MyPlugin : IIvyPlugin
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.MyPlugin",
        Name = "My Plugin",
        ConfigSectionName = "MyPlugin",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Puzzle"),
    };

    public PluginConfigurationSchema? ConfigurationSchema { get; } = new()
    {
        Fields =
        [
            new()
            {
                Key = "ApiKey",
                Type = ConfigFieldType.Secret,
                IsRequired = true,
                Description = "Your API key"
            }
        ]
    };

    public void Configure(IIvyPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;

        if (context is not ITendrilPluginContext tendrilContext)
            return;

        // Register services, messaging channels, etc.
    }
}
```

### 3. Build and Package

```bash
dotnet build
```

The compiled DLL lands in `bin/Debug/net10.0/`. To package for distribution:

```bash
dotnet pack -c Release
```

This produces a `.nupkg` file for NuGet-based distribution. For source plugin development (recommended during development), no packaging is needed — just point a plugin reference at the project directory.

## Testing Locally

### Option A: Plugin References File

The simplest way to test a plugin during development. Create or edit `plugin-references.yaml` in your Tendril plugins directory (`<TENDRIL_HOME>/plugins/plugin-references.yaml`):

```yaml
- /absolute/path/to/your/plugin/project
- ../../relative/path/to/plugin  # relative to the plugins directory
```

**How it works:**
- Tendril watches this file for changes (debounced at 500ms)
- Adding a path triggers plugin load; removing a path triggers unload
- If the referenced directory contains a `.csproj` file, Tendril automatically runs `dotnet build` before loading
- File watchers on referenced directories detect source changes (`.cs`, `.csproj`, `.razor`, `.props`, `.targets`) and trigger rebuild + reload
- DLL changes in `bin/` subdirectories trigger reload without rebuild

**Example** (from the Ivy team's own config):
```yaml
- ../../Ivy-Tendril/src/plugins/plugins/Ivy.Tendril.Plugin.Slack
- ../../Ivy-Tendril/src/plugins/plugins/Ivy.Tendril.Plugin.Linear
```

### Option B: Local NuGet Package

For testing the packaged form of your plugin:

1. Build a NuGet package: `dotnet pack -c Release`
2. Copy the resulting `.nupkg` to a local feed or extract the DLLs
3. Place the plugin DLLs in a subdirectory under `<TENDRIL_HOME>/plugins/`

The directory name should match your main assembly name (e.g., `plugins/Ivy.Tendril.Plugin.MyPlugin/`).

## Submitting to the Tendril Marketplace

> **To be implemented...** The Tendril Marketplace submission process, code policy enforcement, and automated review pipeline are planned but not yet available.

### Package Requirements

> **To be implemented...**

### Code Policy

> **To be implemented...**

### Submission Process

> **To be implemented...**

## Plugin Interfaces

### The Plugin Manifest

Every plugin exposes a `PluginManifest` record describing its identity:

```csharp
public record PluginManifest
{
    public required string Id { get; init; }              // Unique ID (e.g., "Ivy.Tendril.Plugin.Linear")
    public required string Name { get; init; }            // Display name (e.g., "Linear")
    public required string ConfigSectionName { get; init; } // Key in plugin-config.yaml
    public required Version Version { get; init; }        // Semantic version
    public Version? MinimumHostVersion { get; init; }     // Minimum Tendril version required
    public string[] Dependencies { get; init; } = [];     // IDs of plugins this one depends on
    public PluginIcon? Icon { get; init; }                // Display icon
}
```

**Notes:**
- `Id` must be globally unique. Convention: `Ivy.Tendril.Plugin.<Name>` for first-party, `<Org>.Tendril.Plugin.<Name>` for third-party.
- `Dependencies` are resolved via topological sort — dependent plugins are loaded first.
- If `MinimumHostVersion` is set and the host is older, the plugin is skipped with an error log.

### Plugin Icons

Icons are specified via the `PluginIcon` record with three kinds:

```csharp
// Use a built-in icon name (matches the Icons enum)
Icon = PluginIcon.Named("Linear")

// Use an external image URL
Icon = PluginIcon.Url("https://example.com/icon.png")

// Use a file bundled with the plugin (relative to plugin directory)
Icon = PluginIcon.File("assets/icon.svg")
```

For `PluginIcon.File`, the asset is served at runtime via `/ivy/plugins/{pluginId}/assets/{relativePath}`. Supported formats: SVG, PNG, JPG, GIF, WebP, ICO.

The plugin loader validates file-based icons at load time and logs a warning if the referenced file doesn't exist.

### Configuration Schema

Plugins declare their configuration requirements with `PluginConfigurationSchema`:

```csharp
public PluginConfigurationSchema? ConfigurationSchema { get; } = new()
{
    Fields =
    [
        new()
        {
            Key = "BotToken",
            Type = ConfigFieldType.Secret,
            IsRequired = true,
            Description = "Slack Bot User OAuth Token (starts with xoxb-)"
        },
        new()
        {
            Key = "DefaultChannel",
            Type = ConfigFieldType.String,
            IsRequired = false,
            Description = "Default channel ID or name for messages",
            DefaultValue = "general"
        },
        new()
        {
            Key = "MaxRetries",
            Type = ConfigFieldType.Integer,
            IsRequired = false,
            Description = "Maximum number of retry attempts",
            DefaultValue = "3"
        },
        new()
        {
            Key = "Enabled",
            Type = ConfigFieldType.Boolean,
            IsRequired = false,
            DefaultValue = "true",
            Description = "Whether the plugin is active"
        }
    ]
};
```

**Field types:** `ConfigFieldType.String`, `ConfigFieldType.Integer`, `ConfigFieldType.Boolean`, `ConfigFieldType.Secret`

**Behavior:**
- Required fields missing a value cause the plugin to load as "Unconfigured" (not activated)
- `DefaultValue` is returned by `Config.GetValue(key)` when no explicit value is set
- Secret fields are rendered as password inputs in the UI
- Values are persisted in `<TENDRIL_HOME>/plugins/plugin-config.yaml` under the plugin's `ConfigSectionName`

**Runtime config access:**
```csharp
public void Configure(IIvyPluginContext context)
{
    string? value = context.Config.GetValue("BotToken");
    context.Config.SetValue("BotToken", "new-value");
    context.Config.RemoveValue("BotToken");
    context.Config.Save(); // Triggers reconfiguration
}
```

### Custom Configuration Views

For plugins that need more than auto-generated forms, override `BuildConfigurationView`:

```csharp
public class MyPlugin : IIvyPlugin
{
    // ... Manifest, ConfigurationSchema ...

    public object? BuildConfigurationView(IIvyPluginConfig configWriter)
    {
        // Return an Ivy view (ViewBase subclass) for custom configuration UI
        return new MyCustomConfigView(configWriter);
    }
}
```

When this method returns a non-null value, the Plugins settings page renders your custom view instead of the auto-generated field list. The `configWriter` allows your view to read/write configuration values and call `Save()` to trigger reconfiguration.

## Hooks: Lifecycle and Scheduling

### Startup and Shutdown

The plugin lifecycle has a single entry point: `Configure(IIvyPluginContext context)`. This is called:
- During initial Tendril startup (after configuration validation passes)
- On plugin reload (after removing old contributions)
- On reconfiguration (when config values change and validation passes)

There is no explicit shutdown hook. When a plugin is unloaded or reloaded:
- All contributions (menu items, dialogs, apps, services) are automatically removed
- The plugin's `AssemblyLoadContext` is unloaded
- Any `IDisposable` service providers are disposed

> **Note:** Explicit `IDisposable` support on the plugin class itself and async shutdown hooks are not yet available.

### Scheduled Execution

> **To be implemented...** Scheduled/recurring plugin execution (e.g., cron-style hooks) is not yet available.

### Before/After Promptware

> **To be implemented...** Hooks that fire before or after promptware execution are not yet available.

## UX: Contributing UI Elements

To contribute UI elements, your plugin needs the Extended Abstractions package:

```bash
dotnet add package Ivy.Tendril.Plugin.Extended.Abstractions
```

Then access the extended context in `Configure()`:

```csharp
public void Configure(IIvyPluginContext context)
{
    if (context is not ITendrilPluginContext tendrilContext)
        return;

    var extendedContext = context.AsTendrilExtendedContext();
    // Now you can add menu items, register dialogs, etc.
}
```

### Adding a Settings Menu Item

```csharp
extendedContext.AddSettingsMenuItem(
    MenuItem.Default("Import Issues from Linear")
        .Tag("$linear-import-issues")    // Required: used for stable sorting
        .Icon(Icons.Download)
        .OnSelect(() => openImportDialog()),
    FooterMenuPosition.ImportIssues);
```

**FooterMenuPosition options:**
- `Top` — before all built-in items
- `Bottom` — after all built-in items
- `ImportIssues` — after the "Import Issues from GitHub" item

Items within the same position bucket are sorted alphabetically by `Tag`.

### Adding a Dialog

Register a dialog factory that returns a view. You receive an `IState<bool>` to control open/close:

```csharp
var openImportDialog = extendedContext.RegisterDialog(
    "$my-plugin-dialog",
    dialogOpen => new MyDialog(dialogOpen));

// Later, invoke the returned Action to open it:
openImportDialog();
```

Your dialog is a standard `ViewBase` subclass with full access to the Ivy widget framework:

```csharp
internal class MyDialog(IState<bool> dialogOpen) : ViewBase
{
    public override object? Build()
    {
        var name = UseState("");

        return new Dialog(dialogOpen,
            header: new DialogHeader("My Dialog"),
            body: new DialogBody(
                Layout.Vertical(
                    new Text("Enter your name:"),
                    new TextInput(name)
                )
            ),
            footer: new DialogFooter(
                new Button("Close", () => dialogOpen.Value = false)
            )
        );
    }
}
```

### Adding a Main Menu App

Plugins can register full applications that appear in Tendril's main navigation:

```csharp
public void Configure(IIvyPluginContext context)
{
    if (context is not IIvyExtendedPluginContext extendedContext)
        return;

    // Register a single app
    extendedContext.AddApp(new AppDescriptor
    {
        Id = "my-plugin-app",
        Label = "My App",
        // ... view factory, icon, etc.
    });

    // Or discover apps from the plugin assembly via [App] attributes
    extendedContext.AddAppsFromAssembly(typeof(MyPlugin).Assembly);
}
```

Additional extended context capabilities:
- `AddMenuItems(transformer)` — modify the main menu item list
- `AddBadgeProvider(menuTag, countProvider)` — add notification badges to menu items
- `UseWebApplication(configure)` — add ASP.NET middleware to the host pipeline
- `UseWebApplicationBuilder(configure)` — configure the host's `WebApplicationBuilder`

### External Widgets

> **To be implemented...** A dedicated external widget system for Tendril plugins (e.g., React-backed custom components contributed by plugins) is not yet available. Plugins can use the standard Ivy widget framework (`ViewBase`, layouts, inputs, etc.) for their dialogs and apps.

### OAuth Integration

> **To be implemented...** A built-in OAuth flow (redirect-based token acquisition) for plugins is not yet available.

Currently, plugins handle authentication via `ConfigFieldType.Secret` fields where users manually paste API tokens or keys:

```csharp
new ConfigFieldDefinition
{
    Key = "ApiKey",
    Type = ConfigFieldType.Secret,
    IsRequired = true,
    Description = "Linear API key (starts with lin_api_)"
}
```

The token is then retrieved in `Configure()` via `context.Config.GetValue("ApiKey")`.

## Services: Extending Tendril Capabilities

### Registering a Messaging Channel

Plugins can register messaging channels that Tendril uses to send notifications (e.g., plan completion updates):

```csharp
public void Configure(IIvyPluginContext context)
{
    if (context is not ITendrilPluginContext tendrilContext)
        return;

    var config = new SlackConfig
    {
        BotToken = context.Config.GetValue("BotToken")!,
        DefaultChannel = context.Config.GetValue("DefaultChannel")!,
    };

    tendrilContext.RegisterMessagingChannel(new SlackMessagingChannel(config));
}
```

Implement the `IMessagingChannel` interface:

```csharp
public interface IMessagingChannel
{
    string Platform { get; }            // e.g., "slack"
    string? DefaultChannel { get; }     // fallback channel

    Task<MessageResult> SendMessageAsync(
        string channel, Message message, CancellationToken ct = default);

    Task DeleteMessageAsync(
        string channel, string messageId, CancellationToken ct = default);

    Task<MessageResult> UploadFileAsync(
        string channel, Stream content, string fileName,
        string? title = null, string? threadId = null, CancellationToken ct = default);
}
```

**Building messages** with the fluent `MessageBuilder`:

```csharp
var message = new MessageBuilder()
    .Bold("Plan completed!")
    .LineBreak()
    .Text("Your plan ")
    .Code("fix-login-bug")
    .Text(" finished successfully.")
    .Divider()
    .Section(s => s
        .Text("Duration: 3m 42s")
        .Text(" | Files changed: 4"))
    .Attach(pdfBytes, "report.pdf", "Execution Report")
    .Build(threadId: existingThreadId);
```

**Message content nodes:** `TextNode`, `BoldNode`, `ItalicNode`, `StrikethroughNode`, `CodeNode`, `CodeBlockNode`, `LinkNode`, `ImageNode`, `LineBreakNode`, `DividerNode`, `SectionNode`, `SequenceNode`

**MessageResult** returned from send/upload:
```csharp
public record MessageResult
{
    public required string MessageId { get; init; }
    public required string ThreadId { get; init; }
    public required string Channel { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

### Updating Ivy Configuration

> **To be implemented...** A plugin API for modifying Tendril/Ivy configuration programmatically is not yet available.

### Installing a Promptware Verification

> **To be implemented...** A plugin API for registering custom promptware verification steps is not yet available.

## Complete Example: Linear Plugin

The Linear plugin demonstrates a complete Tendril plugin with UI contributions and external API integration:

**`LinearPlugin.cs`:**
```csharp
using Ivy.Plugins;
using Ivy.Tendril.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.Linear.LinearPlugin))]

namespace Ivy.Tendril.Plugin.Linear;

public class LinearPlugin : IIvyPlugin
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.Linear",
        Name = "Linear",
        ConfigSectionName = "Linear",
        Version = new Version(1, 0, 0),
        Icon = PluginIcon.Named("Linear"),
    };

    public PluginConfigurationSchema ConfigurationSchema { get; } = new()
    {
        Fields =
        [
            new()
            {
                Key = "ApiKey",
                Type = ConfigFieldType.Secret,
                IsRequired = true,
                Description = "Linear API key (starts with lin_api_)"
            }
        ]
    };

    public void Configure(IIvyPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;

        if (context is not ITendrilPluginContext tendrilContext)
            return;

        var extendedContext = context.AsTendrilExtendedContext();
        var clientFactory = new LinearClientFactory(apiKey);
        var tendrilHome = tendrilContext.TendrilHome;

        var openImportDialog = extendedContext.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, tendrilHome));

        extendedContext.AddSettingsMenuItem(
            MenuItem.Default("Import Issues from Linear")
                .Tag("$linear-import-issues")
                .Icon(Icons.Download)
                .OnSelect(() => openImportDialog()),
            FooterMenuPosition.ImportIssues);
    }
}
```

**What this plugin does:**
1. Declares a secret configuration field for the Linear API key
2. Creates a GraphQL client factory using that key
3. Registers a dialog that lets users browse and import Linear issues
4. Adds a settings menu item to open that dialog

**Project structure:**
```
Ivy.Tendril.Plugin.Linear/
├── Ivy.Tendril.Plugin.Linear.csproj
├── LinearPlugin.cs
├── LinearClientFactory.cs
├── ImportFromLinearDialog.cs
├── GraphQL/
│   ├── schema.graphql
│   ├── operations.graphql
│   └── Generated/          # StrawberryShake generated client
└── .graphqlrc.json
```

**`.csproj` (external developer version):**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Ivy.Tendril.Plugin.Abstractions" Version="1.0.34" />
    <PackageReference Include="Ivy.Tendril.Plugin.Extended.Abstractions" Version="1.0.34" />
    <!-- Plugin-specific dependencies -->
    <PackageReference Include="StrawberryShake.Transport.Http" Version="16.0.7" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.5" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.5" />
  </ItemGroup>
</Project>
```

> **Important:** Set `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` so all dependencies are copied to the output directory. The plugin loader needs all DLLs in one place.

## NuGet Packages

| Package | Description | Use When |
|---------|-------------|----------|
| `Ivy.Tendril.Plugin.Abstractions` | Core Tendril plugin interfaces: `ITendrilPluginContext`, `IMessagingChannel`, messaging types | Your plugin needs Tendril-specific features (messaging, `TendrilHome` access) |
| `Ivy.Tendril.Plugin.Extended.Abstractions` | UI contribution interfaces: `ITendrilExtendedPluginContext`, settings menu, dialogs | Your plugin contributes UI elements (menu items, dialogs) |
| `Ivy.Plugin.Abstractions` | Base Ivy plugin interfaces: `IIvyPlugin`, `IIvyPluginContext`, manifest, config | Transitive dependency — included automatically |

**Version compatibility:**
- `Ivy.Tendril.Plugin.Abstractions` depends on `Ivy.Plugin.Abstractions`
- `Ivy.Tendril.Plugin.Extended.Abstractions` depends on `Ivy` (the full framework, for `ViewBase`, `MenuItem`, etc.)
- Both Tendril packages are versioned together (currently v1.0.34)

**Target framework:** All plugins must target `net10.0`.

## FAQ

**Q: Where does Tendril look for plugins?**
A: In `<TENDRIL_HOME>/plugins/` — both subdirectories containing DLLs and paths listed in `plugin-references.yaml`.

**Q: Can I have multiple plugins in one assembly?**
A: No. Each assembly must have exactly one `[assembly: IvyPlugin(typeof(...))]` attribute. Multiple attributes in a directory cause the loader to skip it.

**Q: How do I debug my plugin?**
A: Use the plugin references file to point at your source project. Tendril will build it automatically and load it. Attach a debugger to the Tendril process, or use logging via `ILogger` (inject via constructor or `UseService<ILogger<T>>()`).

**Q: What happens if my plugin's configuration is invalid?**
A: The plugin loads as "Unconfigured" — it appears in the Plugins settings page but `Configure()` is not called. Once the user provides valid configuration and saves, `ReconfigurePlugin` is called and the plugin activates.

**Q: Can plugins depend on other plugins?**
A: Yes. Set `Dependencies = ["Other.Plugin.Id"]` in your manifest. The loader topologically sorts plugins so dependencies are configured first. Circular dependencies are detected and logged as errors.

**Q: How does hot-reload work?**
A: For referenced plugins, file watchers detect DLL changes in `bin/` directories and source file changes. Source changes trigger `dotnet build`, then the plugin is unloaded (contributions removed, `AssemblyLoadContext` unloaded) and reloaded fresh. This happens without restarting Tendril.

**Q: What assemblies are shared between host and plugins?**
A: `Ivy.Plugin.Abstractions`, `Ivy`, `Ivy.Tendril.Plugin.Abstractions`, and `Ivy.Tendril.Plugin.Extended.Abstractions` are loaded from the host. Do not bundle these in your plugin output — they'll be skipped during loading anyway.

**Q: Can my plugin serve static files (images, CSS, JS)?**
A: Yes. Place files in your plugin directory and reference them via `PluginIcon.File("relative/path")` for icons, or use the built-in asset endpoint at `/ivy/plugins/{pluginId}/assets/{filePath}` for other resources.
