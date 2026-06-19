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
- `Ivy.Plugin.Abstractions` — base interfaces shared across all Ivy hosts (`IIvyPlugin`, `IIvyPlugin<TContext>`, `IIvyPluginContext`)
- `Ivy.Tendril.Plugin.Abstractions` — Tendril-specific interfaces (`ITendrilPluginContext`, `IMessagingChannel`)
- `Ivy.Tendril.Plugin.Extended.Abstractions` — UI contribution interfaces (`ITendrilExtendedPluginContext`, which extends both `IIvyExtendedPluginContext` and `ITendrilPluginContext`)

## Quick Start: Hello World Plugin

### 1. Use the GitHub Template

Create a new plugin repository from the official template:

1. Go to [Ivy-Tendril-Plugin-Template](https://github.com/Ivy-Interactive/Ivy-Tendril-Plugin-Template) on GitHub
2. Click **"Use this template"** → **"Create a new repository"**
3. Clone your new repository and rename the project to match your plugin:

```bash
git clone https://github.com/your-org/your-plugin.git
cd your-plugin
# Rename files and update namespaces from "Template" to your plugin name
mv Ivy.Tendril.Plugin.Template.csproj Ivy.Tendril.Plugin.MyPlugin.csproj
mv TemplatePlugin.cs MyPlugin.cs
```

4. Update the `.csproj` metadata (`PackageId`, `Authors`, `Description`, URLs) and namespaces in your plugin class

The template includes the correct target framework (`net10.0`), `CopyLocalLockFileAssemblies`, and a reference to `Ivy.Tendril.Plugin.Abstractions`.

### 2. Implement Your Plugin

Create a single class implementing `IIvyPlugin<TContext>` and mark the assembly with `[IvyPlugin]`:

```csharp
using Ivy.Plugins;
using Ivy.Tendril.Plugins;

[assembly: IvyPlugin(typeof(Ivy.Tendril.Plugin.MyPlugin.MyPlugin))]

namespace Ivy.Tendril.Plugin.MyPlugin;

public class MyPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.MyPlugin",
        Title = "My Plugin",
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

    public void Configure(ITendrilExtendedPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;

        // Register services, messaging channels, dialogs, etc.
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
- Tendril watches this file for changes
- Adding a path triggers plugin load; removing a path triggers unload
- If the referenced directory contains a `.csproj` file, Tendril automatically runs `dotnet build` before loading
- File watchers on referenced directories detect source changes (`.cs`, `.csproj`, `.razor`, `.props`, `.targets`) and trigger rebuild + reload
- DLL changes in `bin/` subdirectories trigger reload without rebuild

**Example** (from the Ivy team's own config):
```yaml
- ../../Ivy-Tendril/src/plugins/plugins/Ivy.Tendril.Plugin.Slack
- ../../Ivy-Tendril/src/plugins/plugins/Ivy.Tendril.Plugin.Linear
```

**Note:** You can also place source plugins directly in your Tendril plugins directory, and they will be watched and reloaded in the same manner as referenced plugins.

### Option B: Local NuGet Package

For testing the packaged form of your plugin:

1. Build a NuGet package: `dotnet pack -c Release`
2. Copy the resulting `.nupkg` to a local feed or extract the DLLs
3. Place the plugin DLLs in a subdirectory under `<TENDRIL_HOME>/plugins/`

The directory name should match your main assembly name (e.g., `plugins/Ivy.Tendril.Plugin.MyPlugin/`).

## Submitting to the Tendril Marketplace

The Tendril Marketplace is a curated catalog of approved plugins. Plugins are distributed as NuGet packages and go through an approval process before appearing in the marketplace.

### Package Requirements

Your plugin must be published as a NuGet package on [nuget.org](https://www.nuget.org). The following metadata is pulled automatically from your NuGet listing:

| Field | Source | Notes |
|-------|--------|-------|
| Package ID | `.csproj` `<PackageId>` | Must be globally unique, max 128 chars |
| Title | `.csproj` `<Title>` | Display name, max 256 chars |
| Author | `.csproj` `<Authors>` | Comma-separated, max 256 chars |
| Description | `.csproj` `<Description>` | Max 2000 chars |
| Icon URL | `.csproj` `<PackageIcon>` or `<PackageIconUrl>` | Displayed in marketplace UI |
| Project URL | `.csproj` `<PackageProjectUrl>` | Link to source/docs |
| Tags | `.csproj` `<PackageTags>` | Comma-separated categories |
| License | `.csproj` `<PackageLicenseExpression>` | e.g., `Apache-2.0`, `MIT` |

**Additional requirements:**
- Target framework must be `net10.0`
- Set `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
- Must contain exactly one `[assembly: IvyPlugin(typeof(...))]` attribute
- Package must build cleanly and contain all runtime dependencies

### Code Policy

Each plugin version submitted to the marketplace goes through a **three-state approval workflow**:

| State | Description |
|-------|-------------|
| **Undecided** | Default state for newly submitted versions. Not available to users. |
| **Approved** | Version has passed review and is available in the marketplace. |
| **Denied** | Version was rejected. Not available to users. |

**Package integrity:** A SHA256 hash of the `.nupkg` file is computed and stored at submission time. This hash is used to verify package integrity during installation.

> **To be implemented...** Automated code policy enforcement (static analysis, dependency scanning, sandboxing) is planned but not yet available. Currently, approval is a manual process.

### Submission Process

Currently, plugin submission is managed by the Ivy team:

1. **Publish your plugin** to nuget.org (e.g., `dotnet nuget push`)
2. **Contact the Ivy team** to request addition to the marketplace
3. The team registers your package ID and pulls metadata from NuGet
4. A specific version is added and its `.nupkg` hash is recorded
5. The version enters **Undecided** state pending review
6. Upon approval, the plugin becomes available in the marketplace catalog

> **To be implemented...** A self-service submission API and CLI command for plugin authors to submit directly is planned. The current workflow requires manual coordination with the Ivy team.

## Plugin Interfaces

### The Generic `IIvyPlugin<TContext>` Interface

Tendril plugins implement the generic `IIvyPlugin<TContext>` interface, which provides compile-time type safety — you declare the context type your plugin requires, and receive it directly in `Configure()` without manual casting:

```csharp
public class MyPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    // ... Manifest, ConfigurationSchema ...

    public void Configure(ITendrilExtendedPluginContext context)
    {
        context.RegisterDialog(...);
        context.TransformSettingsMenuItems(...);
    }
}
```

**Available Tendril context types:**
| Type | Package | Features |
|------|---------|----------|
| `ITendrilExtendedPluginContext` | `Ivy.Tendril.Plugin.Extended.Abstractions` | UI contributions (dialogs, menu items, apps) + messaging + TendrilHome |
| `ITendrilPluginContext` | `Ivy.Tendril.Plugin.Abstractions` | Messaging channels + TendrilHome access |

Use `ITendrilExtendedPluginContext` if your plugin contributes UI elements (dialogs, menu items, apps). Use `ITendrilPluginContext` if your plugin only needs messaging or TendrilHome access.

If a plugin declares a context type that the host cannot provide, an `InvalidOperationException` is thrown at load time with a clear error message.

### The Plugin Manifest

Every plugin exposes a `PluginManifest` record describing its identity:

```csharp
public record PluginManifest
{
    public required string Id { get; init; }              // Unique ID (e.g., "Ivy.Tendril.Plugin.Linear")
    public required string Title { get; init; }           // Display name (e.g., "Linear")
    public required Version Version { get; init; }        // Semantic version
    public Version? MinimumHostVersion { get; init; }     // Minimum Tendril version required
    public PluginIcon? Icon { get; init; }                // Display icon
}
```

**Notes:**
- `Id` must be globally unique. Convention: `Ivy.Tendril.Plugin.<Name>` for first-party, `<Org>.Tendril.Plugin.<Name>` for third-party.
- If `MinimumHostVersion` is set and the host is older, the plugin is skipped with an error log.

### Plugin Icons

Icons are specified via the `PluginIcon` record with three kinds:

```csharp
// Use a built-in icon name (matches the Icons enum in Ivy-Framework)
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
- Values are persisted in `<TENDRIL_HOME>/plugins/plugin-config.yaml` under the plugin's `Id`

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
public class MyPlugin : IIvyPlugin<ITendrilPluginContext>
{
    // ... Manifest, ConfigurationSchema, Configure ...

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

The plugin lifecycle has a single entry point: `Configure(...)`. When using the generic interface, the parameter type matches `TContext`. This method is called:
- During initial Tendril startup (after configuration validation passes)
- On plugin reload (after removing old contributions)
- On reconfiguration (when config values change and validation passes)

Plugins can implement the `ShutdownAsync` method to perform cleanup before being unloaded:

```csharp
public class MyPlugin : IIvyPlugin<ITendrilPluginContext>
{
    private WebSocket? _connection;

    // ... Manifest, ConfigurationSchema, Configure ...

    public async Task ShutdownAsync(PluginShutdownContext context)
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Plugin shutting down",
                context.CancellationToken);
        }

        context.Logger.LogInformation("Disconnected cleanly");
    }
}
```

`ShutdownAsync` is called:
- Before a plugin is unloaded (`PluginShutdownReason.Unload`)
- Before a plugin is reloaded (`PluginShutdownReason.Reload`)
- Before a plugin is reconfigured (`PluginShutdownReason.Reconfigure`)
- When the host application exits (`PluginShutdownReason.HostExit`)

The `PluginShutdownContext` provides:
- `CancellationToken` — cancelled after a 5-second timeout
- `Reason` — why the plugin is being shut down
- `Logger` — for logging during shutdown

After the timeout expires, the plugin is forcefully unloaded regardless of whether `ShutdownAsync` completed. Exceptions are logged and swallowed — one plugin's failure does not affect others.

After `ShutdownAsync` completes (or times out):
- All contributions (menu items, dialogs, apps, services) are automatically removed
- The plugin's `AssemblyLoadContext` is unloaded
- Any `IDisposable` service providers are disposed

### Scheduled Execution

> **To be implemented...** Scheduled/recurring plugin execution (e.g., cron-style hooks) is not yet available.

### Before/After Promptware

> **To be implemented...** Hooks that fire before or after promptware execution are not yet available.

## UX: Contributing UI Elements

To contribute UI elements, your plugin needs the Extended Abstractions package:

```bash
dotnet add package Ivy.Tendril.Plugin.Extended.Abstractions
```

Then implement `IIvyPlugin<ITendrilExtendedPluginContext>` to receive the extended context directly:

```csharp
public class MyPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    // ... Manifest, ConfigurationSchema ...

    public void Configure(ITendrilExtendedPluginContext context)
    {
        // Add menu items, register dialogs, access TendrilHome, etc.
    }
}
```

### Modifying the Settings Menu

Use `TransformSettingsMenuItems` to transform the footer settings menu. Transformers receive the current menu items and return a modified list, sorted by priority (lower = first):

```csharp
context.TransformSettingsMenuItems(items =>
{
    var list = items.ToList();
    var importIndex = list.FindIndex(m => (string?)m.Tag == "$import-issues");
    var insertAt = importIndex >= 0 ? importIndex + 1 : list.Count;
    list.Insert(insertAt,
        MenuItem.Default("Import Issues from Linear")
            .Tag("$linear-import-issues")
            .Icon(Icons.Download)
            .OnSelect(() => openImportDialog()));
    return list;
});
```

Built-in settings menu tags: `$setup`, `$trash`, `$import-issues`, `$theme`, `$debug` (debug builds only).

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

Plugins can register apps that appear in Tendril's main navigation. Use `IIvyPlugin<ITendrilExtendedPluginContext>` (or `IIvyPlugin<IIvyExtendedPluginContext>` for host-agnostic plugins):

```csharp
public void Configure(ITendrilExtendedPluginContext context)
{
    // Register a single app
    context.AddApp(new AppDescriptor
    {
        Id = "my-plugin-app",
        Label = "My App",
        // ... view factory, icon, etc.
    });

    // Or discover apps from the plugin assembly via [App] attributes
    context.AddAppsFromAssembly(typeof(MyPlugin).Assembly);
}
```

Additional extended context capabilities:
- `AddBadgeProvider(menuTag, countProvider)` — add notification badges to menu items
- `UseWebApplication(configure)` — add ASP.NET middleware to the host pipeline
- `UseWebApplicationBuilder(configure)` — configure the host's `WebApplicationBuilder`

### External Widgets

Plugins can ship custom React-backed widgets using the `[ExternalWidget]` attribute. The host automatically registers widget assemblies on plugin load and unregisters them on unload.

**C# widget definition:**

```csharp
[ExternalWidget("frontend/dist/Ivy_Tendril_Plugin_SampleWidget.js", ExportName = "Counter")]
public record CounterWidget : WidgetBase<CounterWidget>
{
    [Prop] public string Label { get; init; } = "Count";
    [Prop] public int InitialValue { get; init; } = 0;
    [Prop] public int Step { get; init; } = 1;
}
```

**React component (`frontend/src/Counter.tsx`):**

```tsx
import React, { useState } from "react";

interface CounterProps {
  id: string;
  label?: string;
  initialValue?: number;
  step?: number;
}

export const Counter: React.FC<CounterProps> = ({ label = "Count", initialValue = 0, step = 1 }) => {
  const [count, setCount] = useState(initialValue);
  return (
    <div>
      <span>{label}: {count}</span>
      <button onClick={() => setCount(c => c - step)}>−</button>
      <button onClick={() => setCount(c => c + step)}>+</button>
    </div>
  );
};
```

**Frontend entry (`frontend/src/index.ts`):**

```ts
import { Counter } from "./Counter";

if (typeof window !== "undefined") {
  (window as unknown as Record<string, unknown>).Ivy_Tendril_Plugin_SampleWidget = { Counter };
}

export { Counter };
```

**Project setup:**

- The `.csproj` must import `Ivy.ExternalWidget.targets` to embed the built JS as a resource:
  ```xml
  <Import Project="path/to/Ivy.ExternalWidget.targets" Condition="'$(IvySource)' == 'true'" />
  ```
- The `vite.config.ts` must build as IIFE with `name` matching the namespace (dots replaced with underscores)
- React, ReactDOM, and react/jsx-runtime are externalized (provided by the host)

**How it works:**

1. The frontend is built with `pnpm build` → produces an IIFE bundle in `frontend/dist/`
2. The MSBuild targets embed `frontend/dist/**` as assembly resources
3. On plugin load, `ExternalWidgetRegistry.RegisterAssembly()` scans for `[ExternalWidget]` types
4. The host serves the JS bundle at `/ivy/external-widgets/{typeName}/script.js`
5. The frontend loads the bundle and renders the exported React component

See `Ivy.Tendril.Plugin.SampleWidget` for a complete working example.

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

> **To be implemented...** Although messaging channels can be implemented and registered, Tendril does not use them yet.

Plugins can register messaging channels that Tendril can use to send notifications (e.g., plan completion updates). Use `IIvyPlugin<ITendrilPluginContext>` (or `ITendrilExtendedPluginContext` if you also need UI contributions):

```csharp
public class SlackPlugin : IIvyPlugin<ITendrilPluginContext>
{
    // ... Manifest, ConfigurationSchema ...

    public void Configure(ITendrilPluginContext context)
    {
        var config = new SlackConfig
        {
            BotToken = context.Config.GetValue("BotToken")!,
            DefaultChannel = context.Config.GetValue("DefaultChannel")!,
        };

        context.RegisterMessagingChannel(new SlackMessagingChannel(config));
    }
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

public class LinearPlugin : IIvyPlugin<ITendrilExtendedPluginContext>
{
    public PluginManifest Manifest { get; } = new()
    {
        Id = "Ivy.Tendril.Plugin.Linear",
        Title = "Linear",
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

    public void Configure(ITendrilExtendedPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;
        var clientFactory = new LinearClientFactory(apiKey);

        var openImportDialog = context.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, context.TendrilHome));

        context.TransformSettingsMenuItems(items =>
        {
            var list = items.ToList();
            var importIndex = list.FindIndex(m => (string?)m.Tag == "$import-issues");
            var insertAt = importIndex >= 0 ? importIndex + 1 : list.Count;
            list.Insert(insertAt,
                MenuItem.Default("Import Issues from Linear")
                    .Tag("$linear-import-issues")
                    .Icon(Icons.Download)
                    .OnSelect(() => openImportDialog()));
            return list;
        });
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
    <PackageReference Include="Ivy.Tendril.Plugin.Abstractions" Version="1.0.53" />
    <PackageReference Include="Ivy.Tendril.Plugin.Extended.Abstractions" Version="1.0.53" />
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

**Q: How does hot-reload work?**
A: For referenced plugins, file watchers detect DLL changes in `bin/` directories and source file changes. Source changes trigger `dotnet build`, then the plugin is unloaded (contributions removed, `AssemblyLoadContext` unloaded) and reloaded fresh. This happens without restarting Tendril.

**Q: What assemblies are shared between host and plugins?**
A: `Ivy.Plugin.Abstractions`, `Ivy`, `Ivy.Tendril.Plugin.Abstractions`, and `Ivy.Tendril.Plugin.Extended.Abstractions` are loaded from the host. You do not need to bundle these in your plugin output — they'll be skipped during loading anyway.

**Q: Can my plugin serve static files (images, CSS, JS)?**
A: Yes. Place files in your plugin directory and reference them via `PluginIcon.File("relative/path")` for icons, or use the built-in asset endpoint at `/ivy/plugins/{pluginId}/assets/{filePath}` for other resources.
