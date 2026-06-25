# Ivy Tendril Plugin Developer Guide

## Introduction

Ivy Tendril supports plugins that can extend its functionality. Plugins are .NET class libraries that implement the `IIvyPlugin` interface and are loaded dynamically at runtime. They can contribute UI elements, schedule background tasks, add full apps, and more.

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

    public PluginConfigurationSchema? ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret("ApiKey", description: "Your API key", isRequired: true)
        .Build();

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

## Plugin Interfaces

### The `IIvyPlugin<TContext>` Interface

Tendril plugins implement `IIvyPlugin<TContext>`, where `TContext` is the plugin context type your plugin requires:

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
| `ITendrilExtendedPluginContext` | `Ivy.Tendril.Plugin.Extended.Abstractions` | Everything below + UI contributions (dialogs, menu items, apps, widgets) |
| `ITendrilPluginContext` | `Ivy.Tendril.Plugin.Abstractions` | TendrilHome, messaging, scheduled tasks, lifecycle hooks, inbox, config modification, promptware registration |

Use `ITendrilExtendedPluginContext` if your plugin contributes UI elements. Use `ITendrilPluginContext` if your plugin is purely a background service (syncing, notifications, automation).

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

Plugins declare their configuration requirements with `PluginConfigurationSchema`, built using the fluent `SchemaBuilder`:

```csharp
public PluginConfigurationSchema? ConfigurationSchema { get; } = new SchemaBuilder()
    .AddSecret("BotToken", description: "Slack Bot User OAuth Token (starts with xoxb-)", isRequired: true)
    .AddString("DefaultChannel", defaultValue: "general", description: "Default channel ID or name for messages")
    .AddInteger("MaxRetries", defaultValue: 3, description: "Maximum number of retry attempts")
    .AddBoolean("Enabled", defaultValue: true, description: "Whether the plugin is active")
    .Build();
```

**Field types:** `ConfigFieldType.String`, `ConfigFieldType.Integer`, `ConfigFieldType.Boolean`, `ConfigFieldType.Secret`

**Behavior:**
- Required fields missing a value cause the plugin to load as "Unconfigured" (not activated)
- `DefaultValue` is returned by `Config.GetValue(key)` when no explicit value is set
- Secret fields are rendered as password inputs in the UI
- Values are persisted in `<TENDRIL_HOME>/plugins/plugin-config.yaml` under the plugin's `Id`

> **To be implemented...** **Custom validation.** The `SchemaBuilder` handles common cases (required fields, type checking), but some plugins might need richer constraints in the future — conditional requirements ("if A is set, B is required"), format validation ("must start with `lin_api_`"), range checks, or cross-field logic. A planned `ValidateConfig` method on `IIvyPlugin` will let plugins optionally provide custom validation that returns field-keyed errors, enabling inline error display in both the default schema-driven UI and custom configuration views. Plugins that don't need custom validation will continue working unchanged.

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

## Plugin Lifecycle

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

### Scheduled Tasks

> **To be implemented...** This API is designed but not yet available at runtime. The interfaces below describe the intended contract.

Plugins can register recurring tasks that execute on a schedule. This is useful for periodic syncing (e.g., importing issues from GitHub every 15 minutes, polling an external API for updates, refreshing cached data).

**Registering a scheduled task:**

```csharp
public void Configure(ITendrilPluginContext context)
{
    context.RegisterScheduledTask(new ScheduledTaskDescriptor
    {
        Id = "github-issue-sync",
        DisplayName = "Sync GitHub Issues",
        Schedule = Schedule.Every(TimeSpan.FromMinutes(15)),
        OverlapPolicy = OverlapPolicy.Skip,  // Skip if previous execution still running
        Retry = RetryPolicy.Backoff(maxAttempts: 3, initialDelay: TimeSpan.FromMinutes(2)),
        ExecuteAsync = async (ctx, ct) =>
        {
            var issues = await FetchNewIssues(ct);
            ctx.Inbox.AddRange(issues.Select(issue => new InboxItem
            {
                Description = issue.Body,
                SourceUrl = issue.Url,
                SourceIdentifier = issue.Identifier
            }));

            return ScheduledTaskResult.Success($"Imported {issues.Count} issues");
        }
    });
}
```

**The `ScheduledTaskDescriptor` record:**

```csharp
public record ScheduledTaskDescriptor
{
    /// <summary>Unique ID within this plugin (namespaced automatically by plugin ID at runtime).</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable name shown in the Jobs/Settings UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>When to run.</summary>
    public required Schedule Schedule { get; init; }

    /// <summary>What to do if the previous execution hasn't finished when the next tick fires.</summary>
    public OverlapPolicy OverlapPolicy { get; init; } = OverlapPolicy.Skip;

    /// <summary>What to do when execution fails. Default: no retry (wait for next scheduled tick).</summary>
    public RetryPolicy Retry { get; init; } = RetryPolicy.None;

    /// <summary>The async callback to execute on each tick.</summary>
    public required Func<ScheduledTaskContext, CancellationToken, Task<ScheduledTaskResult>> ExecuteAsync { get; init; }

    /// <summary>Whether the task starts enabled. Users can toggle this in Settings.</summary>
    public bool EnabledByDefault { get; init; } = true;
}
```

**Schedule expressions:**

```csharp
// Simple intervals (minimum 1 minute)
Schedule.Every(TimeSpan.FromMinutes(15))
Schedule.Every(TimeSpan.FromHours(1))

// Named presets
Schedule.Hourly
Schedule.Daily(hour: 9)
Schedule.Daily(hour: 9, minute: 30)
```

**Overlap policies:**

| Policy | Behavior |
|--------|----------|
| `Skip` | If the previous execution is still running, skip this tick entirely. Default. |
| `Queue` | Queue the new execution to start immediately after the current one finishes. At most one queued. |
| `Concurrent` | Start the new execution regardless. Use with caution. |

**Retry policy:**

When a task returns `ScheduledTaskResult.Failure(...)`, the retry policy determines what happens next. Without a retry policy, the task simply waits for its next scheduled tick — which could be hours or a day away.

```csharp
// No retry — wait for the next scheduled tick (default)
RetryPolicy.None

// Exponential backoff: retry up to N times with increasing delays
// e.g., 2min → 4min → 8min, then give up until next scheduled tick
RetryPolicy.Backoff(maxAttempts: 3, initialDelay: TimeSpan.FromMinutes(2))

// Fixed interval: retry up to N times with a constant delay between attempts
RetryPolicy.Fixed(maxAttempts: 5, delay: TimeSpan.FromMinutes(5))
```

Retry state is tracked per-task. When all retry attempts are exhausted, `ConsecutiveFailures` on the context reflects the total count (initial attempt + retries). The next scheduled tick resets the retry counter. Retries do not interfere with the overlap policy — if a retry is pending and a normal tick fires, the tick takes priority and resets the retry state.

**Execution context:**

```csharp
public record ScheduledTaskContext
{
    public string TendrilHome { get; }
    public IInbox Inbox { get; }
    public ILogger Logger { get; }
    public DateTimeOffset ScheduledTime { get; }       // When this tick was supposed to fire
    public DateTimeOffset LastSuccessTime { get; }     // When the task last completed successfully
    public int ConsecutiveFailures { get; }            // How many times in a row it has failed
}
```

**Result reporting:**

```csharp
// Success — optional message shown in task history
ScheduledTaskResult.Success("Imported 3 issues")

// Failure — logged, increments ConsecutiveFailures
ScheduledTaskResult.Failure("API returned 401 — check credentials")

// Skipped — the task decided not to do work this tick (not counted as failure)
ScheduledTaskResult.Skipped("No new issues since last sync")
```

**Lifecycle:**
- Scheduled tasks are automatically unregistered when the plugin is unloaded, reloaded, or reconfigured.
- The host cancels any in-flight execution via the `CancellationToken` during shutdown (5-second grace period, same as `ShutdownAsync`).
- Task enable/disable state is persisted in `plugin-config.yaml` under the plugin's ID, so user preferences survive plugin reloads.
- Task execution history (last 50 results per task) is stored in memory and visible in the Settings UI.

**Design notes:**
- Scheduled tasks do NOT create Tendril Jobs — they are lightweight background operations. If your task needs to trigger a full plan creation, write a file to the Inbox instead (the `InboxWatcherService` will pick it up).
- The scheduler uses a single timer wheel shared across all plugins. Minimum interval is 1 minute.
- If Tendril is not running (e.g., machine is off), missed ticks are not replayed. The task simply runs on the next tick after startup.

### Lifecycle Hooks

Plugins can register in-process callbacks that fire at specific points in the Tendril lifecycle. Unlike config-based hooks (which run shell commands), these are code callbacks that execute inside the host process with full access to plugin state.

**Registering hooks (selected examples — see `IPluginHooks` below for the full list):**

```csharp
public void Configure(ITendrilPluginContext context)
{
    // Fire after any promptware job completes
    context.Hooks.AfterJob(async (evt, ct) =>
    {
        if (evt.Status == JobStatus.Completed && evt.JobType == "CreatePr")
            await NotifySlack(evt.PlanFolder, ct);
    });

    // Fire before a plan is created (from inbox, UI, or API)
    context.Hooks.BeforeCreatePlan(async (evt, ct) =>
    {
        // Enrich the plan description with external context
        evt.Description = await EnrichWithLinearContext(evt.Description, ct);
    });

    // Fire when config is about to be saved
    context.Hooks.BeforeConfigSave((evt) =>
    {
        // Validate or reject config changes
        if (evt.NewSettings.MaxConcurrentJobs > 50)
            evt.Reject("MaxConcurrentJobs cannot exceed 50 with current license");
    });
}
```

**The `IPluginHooks` interface:**

```csharp
public interface IPluginHooks
{
    // Job lifecycle
    void BeforeJob(Func<BeforeJobEvent, CancellationToken, Task> handler);
    void AfterJob(Func<AfterJobEvent, CancellationToken, Task> handler);

    // Plan lifecycle
    void BeforeCreatePlan(Func<BeforeCreatePlanEvent, CancellationToken, Task> handler);
    void AfterCreatePlan(Func<AfterCreatePlanEvent, CancellationToken, Task> handler);

    // Configuration
    void BeforeConfigSave(Action<ConfigSaveEvent> handler);
    void AfterConfigReload(Action handler);
}
```

**Hook event types:**

```csharp
public record BeforeJobEvent
{
    public required string JobType { get; init; }      // "CreatePlan", "ExecutePlan", "CreatePr", etc.
    public required string PlanFolder { get; init; }
    public required string Project { get; init; }
    public bool Cancelled { get; private set; }
    public void Cancel(string reason) { /* ... */ }
}

public record AfterJobEvent
{
    public required string JobId { get; init; }
    public required string JobType { get; init; }
    public required JobStatus Status { get; init; }    // Completed, Failed, Stopped, TimedOut
    public required string PlanFolder { get; init; }
    public required string Project { get; init; }
    public int? ExitCode { get; init; }
    public TimeSpan Duration { get; init; }
}

public record BeforeCreatePlanEvent
{
    public string Description { get; set; }            // Mutable — hooks can enrich/transform
    public string Project { get; set; }
    public string? SourceUrl { get; init; }
    public string? SourceIdentifier { get; init; }
    public bool Cancelled { get; private set; }
    public void Cancel(string reason) { /* ... */ }
}

public record AfterCreatePlanEvent
{
    public required string PlanFolder { get; init; }
    public required string PlanId { get; init; }
    public required string Project { get; init; }
    public required string Title { get; init; }
}

public record ConfigSaveEvent
{
    public required TendrilSettings CurrentSettings { get; init; }
    public required TendrilSettings NewSettings { get; init; }
    public bool Rejected { get; private set; }
    public string? RejectionReason { get; private set; }
    public void Reject(string reason) { /* ... */ }
}
```

**Execution semantics:**
- Multiple plugins can register handlers for the same event. They execute in plugin load order.
- `Before*` hooks with `Cancel()` prevent the operation from proceeding. The first cancellation wins.
- `Before*` hooks with mutable properties (like `BeforeCreatePlanEvent.Description`) allow enrichment — each hook sees the result of the previous hook's modifications.
- Hook handlers have a 10-second timeout. If a handler exceeds this, it is cancelled and the operation proceeds without it. Timeouts are logged as warnings.
- Exceptions in hook handlers are caught, logged, and do not prevent the operation from proceeding (fail-open). This ensures one plugin cannot break the system.
- Hooks are automatically unregistered when the plugin is unloaded.

**Relationship to config-based hooks:**

Config-based hooks (`PromptwareHookConfig` in `config.yaml`) run as external shell commands and are owned by the user. Plugin lifecycle hooks run in-process and are owned by the plugin. Both can coexist — plugin hooks always run first, then config-based hooks. This means user-configured hooks see the final state after all plugin transformations, and can override or react to plugin behavior.

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
    // Register an app with a view factory
    context.AddApp(new AppDescriptor
    {
        Id = "my-plugin-app",
        Label = "My App",
        Icon = Icons.BarChart,
        ViewFactory = () => new MyDashboardView()
    });

    // Or discover apps from the plugin assembly via [App] attributes
    context.AddAppsFromAssembly(typeof(MyPlugin).Assembly);
}
```

The `ViewFactory` returns a `ViewBase` subclass that renders the app's content:

```csharp
internal class MyDashboardView : ViewBase
{
    public override object? Build()
    {
        var data = UseQuery("dashboard-data", async (_, ct) => await FetchMetrics(ct));

        return Layout.Vertical().Gap(3)
            | Text.Heading("My Dashboard")
            | (data.Loading ? new Loading() : new DataTable(data.Value));
    }
}
```

Additional extended context capabilities:
- `AddBadgeProvider(menuTag, countProvider)` — add notification badges to menu items
- `UseWebApplication(configure)` — add ASP.NET middleware to the host pipeline
- `UseWebApplicationBuilder(configure)` — configure the host's `WebApplicationBuilder`

### External Widgets

Plugins can ship custom React-backed widgets using the `[ExternalWidget]` attribute. The host automatically registers widget assemblies on plugin load and unregisters them on unload.

For full documentation on building external widgets (C# backend, React frontend, Vite configuration, project setup, multi-widget bundles, troubleshooting), see the [External Widgets](https://github.com/Ivy-Interactive/Ivy-Framework/blob/development/src/Ivy.Docs.Shared/Docs/02_Widgets/07_Advanced/05_ExternalWidgets.md) documentation in the Ivy Framework. You can also scaffold one with the CLI: `ivy widget`.

**Plugin-specific behavior:** On plugin load, the host automatically calls `ExternalWidgetRegistry.RegisterAssembly()` to discover `[ExternalWidget]` types in your plugin assembly. On unload, they are unregistered. No additional wiring is needed beyond defining the widget and building the frontend.

See `Ivy.Tendril.Plugin.SampleWidget` for a complete working example.

### OAuth Integration

> **To be implemented...** A built-in OAuth flow (redirect-based token acquisition) for plugins is not yet available.

Currently, plugins handle authentication via secret fields where users manually paste API tokens or keys:

```csharp
public PluginConfigurationSchema? ConfigurationSchema { get; } = new SchemaBuilder()
    .AddSecret("ApiKey", description: "Linear API key (starts with lin_api_)", isRequired: true)
    .Build();
```

The token is then retrieved in `Configure()` via `context.Config.GetValue("ApiKey")`.

## Services: Extending Tendril Capabilities

### Registering a Messaging Channel

> **To be implemented...** The messaging channel interface can be implemented and registered today, but nothing currently consumes registered channels to send messages. Automatic notifications (e.g., on PR creation or job completion) are planned — the triggering logic may live in Tendril core, in a separate notifications plugin, or in the messaging plugins themselves (using lifecycle hooks to decide when to send).

Plugins can register messaging channels for sending notifications (e.g., plan completion updates, job failures). Use `IIvyPlugin<ITendrilPluginContext>` (or `ITendrilExtendedPluginContext` if you also need UI contributions):

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

### Updating Tendril Configuration

> **To be implemented...** This API is designed but not yet available at runtime. The interfaces below describe the intended contract.

Plugins can programmatically read and modify the main `config.yaml` through a scoped, transactional API. This is useful for plugins that need to install hooks, add verifications, register projects, or adjust settings as part of their setup.

**Accessing the config API:**

```csharp
public void Configure(ITendrilPluginContext context)
{
    // Read current configuration (read-only snapshot)
    var settings = context.TendrilConfig.Current;
    var projects = settings.Projects;
    var hasMyProject = projects.Any(p => p.Name == "MyProject");

    // Modify configuration with a transaction
    context.TendrilConfig.Modify(config =>
    {
        // Add a hook to an existing project
        var project = config.Projects.FirstOrDefault(p => p.Name == "Framework");
        project?.Hooks.Add(new PromptwareHookConfig
        {
            Name = "LinearSync",
            When = "after",
            Promptwares = ["CreatePr"],
            Action = "curl -X POST https://my-webhook.example.com/notify"
        });

        // Add a custom verification
        config.Verifications.Add(new VerificationConfig
        {
            Name = "LinearStatusUpdate",
            Prompt = "Update the corresponding Linear issue status to 'In Review'."
        });
    });
}
```

**The `ITendrilConfigWriter` interface:**

```csharp
public interface ITendrilConfigWriter
{
    /// <summary>Read-only snapshot of the current TendrilSettings.</summary>
    TendrilSettings Current { get; }

    /// <summary>
    /// Apply modifications to config.yaml within a transaction.
    /// The callback receives a mutable clone of the current settings.
    /// Changes are validated, persisted, and a reload is triggered.
    /// Throws ConfigModificationException if validation fails.
    /// </summary>
    void Modify(Action<TendrilSettings> mutator);

    /// <summary>Async variant for mutations that need I/O (e.g., fetching defaults from an API).</summary>
    Task ModifyAsync(Func<TendrilSettings, Task> mutator, CancellationToken ct = default);
}
```

**Transaction semantics:**

- The `mutator` callback receives a **deep clone** of the current settings. Your modifications do not affect the live config until the transaction commits.
- After the mutator returns, the host **validates** the modified settings (same checks as manual config edits — path validation, range checks, duplicate detection).
- If validation passes, the settings are serialized to YAML, written to `config.yaml` (with backup), and a reload is triggered (`SettingsReloaded` event fires).
- If validation fails, a `ConfigModificationException` is thrown with details, and the existing config is untouched.
- Only one `Modify` call executes at a time (serialized via a lock). Concurrent calls queue and each sees the result of the previous.

**More examples:**

```csharp
// Install a hook on all projects
context.TendrilConfig.Modify(config =>
{
    foreach (var project in config.Projects)
    {
        if (project.Hooks.Any(h => h.Name == "MyPluginHook")) continue;
        project.Hooks.Add(new PromptwareHookConfig
        {
            Name = "MyPluginHook",
            When = "after",
            Promptwares = ["CreatePr"],
            Action = "pwsh -NoProfile -File ~/.tendril/hooks/my-plugin-notify.ps1"
        });
    }
});

// Add a verification definition
context.TendrilConfig.Modify(config =>
{
    if (config.Verifications.Any(v => v.Name == "SecurityScan")) return;
    config.Verifications.Add(new VerificationConfig
    {
        Name = "SecurityScan",
        Prompt = "Run a security scan on all changed files using the Snyk CLI."
    });
});
```

**Safety considerations:**

- Plugins should be **additive** — prefer adding entries over removing or replacing existing ones. Users may have manually configured hooks, verifications, or projects that your plugin shouldn't touch.
- Use **idempotent checks** (e.g., `if (already exists) return;`) to avoid duplicating entries on plugin reload.
- The `BeforeConfigSave` lifecycle hook (see Lifecycle Hooks above) fires for config modifications made by plugins too — other plugins can validate or reject your changes.
- Avoid modifying auth, tunnel, or LLM settings unless your plugin is specifically an auth/tunnel/LLM provider. These sections are sensitive and user-managed.

### Adding to the Inbox

Plugins can programmatically add items to the Tendril Inbox, which triggers plan creation. This is the primary way for plugins to feed work into Tendril — whether from external issue trackers, scheduled syncs, webhooks, or user-initiated imports.

**Adding inbox items:**

```csharp
public void Configure(ITendrilPluginContext context)
{
    // Simple: just a description (project auto-detected)
    context.Inbox.Add("Fix the login timeout bug on the settings page");

    // With project and source metadata
    context.Inbox.Add(new InboxItem
    {
        Description = """
            The dashboard chart doesn't render when there are more than 1000 data points.
            It throws a JavaScript heap out of memory error in the browser console.
            """,
        Project = "Framework",
        SourceUrl = "https://linear.app/ivy/issue/IVY-456",
        SourceIdentifier = "IVY-456",
        Labels = ["bug", "performance"]
    });
}
```

**The `IInbox` interface:**

```csharp
public interface IInbox
{
    /// <summary>Add a simple text description to the inbox.</summary>
    void Add(string description);

    /// <summary>Add a structured inbox item with metadata.</summary>
    void Add(InboxItem item);

    /// <summary>Add multiple items at once (avoids thundering herd on the InboxWatcher).</summary>
    void AddRange(IEnumerable<InboxItem> items);
}
```

**The `InboxItem` record:**

```csharp
public record InboxItem
{
    /// <summary>
    /// The task description. This becomes the body of the inbox markdown file
    /// and is passed to the CreatePlan promptware as the task to plan.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Target project name, or "Auto" to let Tendril detect the project from context.
    /// Must match a project name in config.yaml.
    /// </summary>
    public string Project { get; init; } = "Auto";

    /// <summary>URL linking back to the source (e.g., GitHub issue, Linear issue, Jira ticket).</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Short identifier from the source system (e.g., "#123", "IVY-456").</summary>
    public string? SourceIdentifier { get; init; }

    /// <summary>Optional labels for categorization. Written to frontmatter.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];
}
```

**What happens under the hood:**

Calling `context.Inbox.Add(...)` writes a markdown file to `<TendrilHome>/Inbox/` with YAML frontmatter:

```markdown
---
project: Framework
sourceUrl: https://linear.app/ivy/issue/IVY-456
sourceIdentifier: IVY-456
labels: [bug, performance]
---
The dashboard chart doesn't render when there are more than 1000 data points.
It throws a JavaScript heap out of memory error in the browser console.
```

The `InboxWatcherService` detects the new file and creates a `CreatePlan` job from it — exactly the same pipeline as manually dropping a file into the Inbox folder. The API is a convenience layer that handles file naming, frontmatter formatting, and staggered writes (for `AddRange`).

**File naming:** Files are named `<timestamp>-<sanitized-identifier-or-hash>.md` to avoid collisions. If `SourceIdentifier` is set, it's used in the filename (e.g., `20260620T091500-IVY-456.md`); otherwise a short hash of the description is used.

**Typical usage with scheduled tasks:**

```csharp
context.RegisterScheduledTask(new ScheduledTaskDescriptor
{
    Id = "github-issue-sync",
    DisplayName = "Sync GitHub Issues",
    Schedule = Schedule.Every(TimeSpan.FromMinutes(15)),
    ExecuteAsync = async (ctx, ct) =>
    {
        var issues = await FetchNewIssuesFromGitHub(ct);
        var items = issues.Select(issue => new InboxItem
        {
            Description = $"[{issue.Title}]({issue.Url})\n\n{issue.Body}",
            Project = "Auto",
            SourceUrl = issue.Url,
            SourceIdentifier = $"#{issue.Number}"
        });

        ctx.Inbox.AddRange(items);
        return ScheduledTaskResult.Success($"Added {issues.Count} issues to inbox");
    }
});
```

**Deduplication:** The API does **not** deduplicate automatically — if you add the same item twice, two plans will be created. Plugins are responsible for tracking what they've already imported (e.g., using plugin config values, a local JSON log, or checking `SourceIdentifier` against existing plan metadata).

### Installing a Promptware

> **To be implemented...** This API is designed but not yet available at runtime. The interfaces below describe the intended contract.

Plugins can install custom promptwares that become available for use in plan execution, verifications, and hooks. A promptware is an AI-driven program defined by a `Program.md` file that instructs a coding agent what to do, along with optional memory and tool definitions.

**Registering a promptware:**

```csharp
public void Configure(ITendrilPluginContext context)
{
    context.RegisterPromptware(new PromptwareDescriptor
    {
        Name = "SecurityScan",
        DisplayName = "Security Scan",
        Description = "Scans changed files for common security vulnerabilities using Snyk CLI.",
        Program = """
            # SecurityScan

            Scan all files changed by this plan's commits for security vulnerabilities.

            ## Context

            The firmware header contains:
            - **TendrilPlanFolder** — the plan folder path
            - **VerificationDir** — where to write the verification report

            ## Steps

            1. Get the list of changed files from the plan's commits
            2. Run `snyk code test --file=<file>` for each changed file
            3. Write a verification report to `<VerificationDir>/SecurityScan.md`
            """,
        DefaultProfile = ProfileTier.Balanced,
        AllowedTools = ["Bash(snyk *)"],
        Values = [
            new PromptwareValueDescriptor { Name = "VerificationDir", Description = "Output directory for the report", Required = true }
        ]
    });
}
```

**The `PromptwareDescriptor` record:**

```csharp
public record PromptwareDescriptor
{
    /// <summary>
    /// Unique name for this promptware. Used as the folder name under Promptwares/
    /// and referenced in config.yaml promptwares section and verification hooks.
    /// Convention: PascalCase, no spaces (e.g., "SecurityScan", "LinearStatusSync").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Human-readable name for display in Settings UI.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Short description of what this promptware does.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The Program.md content — the AI instructions that define this promptware's behavior.
    /// This is the core of the promptware: it tells the coding agent what to do.
    /// </summary>
    public required string Program { get; init; }

    /// <summary>
    /// Default execution profile. Can be overridden per-project in config.yaml.
    /// </summary>
    public ProfileTier DefaultProfile { get; init; } = ProfileTier.Balanced;

    /// <summary>
    /// Tools the coding agent is allowed to use during execution.
    /// Same syntax as the config.yaml allowedTools field.
    /// </summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = [];

    /// <summary>
    /// Declared value parameters that can be passed to this promptware at runtime.
    /// These become header values accessible in Program.md.
    /// </summary>
    public IReadOnlyList<PromptwareValueDescriptor> Values { get; init; } = [];
}

public record PromptwareValueDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool Required { get; init; }
    public string? DefaultValue { get; init; }
}
```

**How it works:**

When a plugin registers a promptware, the host:
1. Writes `Program.md` to `<TendrilHome>/Promptwares/<Name>/Program.md` (creating the directory if needed)
2. Registers a default config entry in the `promptwares` section (profile + allowedTools) if one doesn't already exist
3. Creates `Memory/` and `Tools/` subdirectories

The promptware then becomes available for:
- **Verifications** — reference it by name in a project's `verifications` list
- **Hooks** — reference it in `promptwares` field of a `PromptwareHookConfig`
- **CLI** — run it with `tendril promptware run <Name> <planFolder> --value Key=Value`
- **Jobs** — the system can invoke it as part of plan execution

**Lifecycle:**
- When the plugin is unloaded, the promptware files are **not** removed, as they may be referenced in existing config entries and plan histories.
- If the plugin is reinstalled and registers the same promptware name, it updates `Program.md` with the new content (preserving `Logs/` and `Memory/`).
- Users can override the profile and allowedTools in `config.yaml` — plugin defaults only apply when no config entry exists.

**Using with the Config API:**

A plugin that installs both a promptware and the corresponding verification wiring:

```csharp
public void Configure(ITendrilPluginContext context)
{
    // Install the promptware
    context.RegisterPromptware(new PromptwareDescriptor
    {
        Name = "SecurityScan",
        DisplayName = "Security Scan",
        Description = "Scans for vulnerabilities using Snyk.",
        Program = LoadEmbeddedProgram("SecurityScan.Program.md"),
        AllowedTools = ["Bash(snyk *)"]
    });

    // Wire it up as a verification in config
    context.TendrilConfig.Modify(config =>
    {
        if (config.Verifications.Any(v => v.Name == "SecurityScan")) return;
        config.Verifications.Add(new VerificationConfig
        {
            Name = "SecurityScan",
            Prompt = """
                Run the SecurityScan promptware:
                `tendril promptware run SecurityScan "<TendrilPlanFolder>" --value VerificationDir="<TendrilPlanFolder>\verification"`
                Read the resulting report and set status based on its Result field.
                """
        });
    });
}
```

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

    public PluginConfigurationSchema ConfigurationSchema { get; } = new SchemaBuilder()
        .AddSecret("ApiKey", description: "Linear API key (starts with lin_api_)", isRequired: true)
        .Build();

    public void Configure(ITendrilExtendedPluginContext context)
    {
        var apiKey = context.Config.GetValue("ApiKey")!;
        var clientFactory = new LinearClientFactory(apiKey);

        var openImportDialog = context.RegisterDialog(
            "$linear-import-dialog",
            dialogOpen => new ImportFromLinearDialog(dialogOpen, clientFactory, context.Inbox));

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
- Both Tendril packages are versioned together

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
