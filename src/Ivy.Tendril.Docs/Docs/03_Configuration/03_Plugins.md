---
icon: Plug
searchHints:
  - plugins
  - marketplace
  - install plugin
  - uninstall
  - plugin-config.yaml
  - plugin-references.yaml
  - nuget
---

# Plugins

<Ingress>
Plugins extend Tendril with new apps, integrations, and automation. Install, configure, and update them from the Plugins settings page.
</Ingress>

## Overview

A plugin is a package that Tendril loads at startup and can add:

- **Apps** in the main sidebar
- **Items** in the Settings menu, often opening a dialog (for example, importing issues from an external tracker)
- **Notification badges** on menu items
- **Background behavior**, such as reacting to job and plan events, or adding items to the Inbox
- **More to come**, as further extension points are planned for future releases

Plugins load and unload while Tendril is running, so installing one does not require a restart.

To open the page, choose **Configuration** from the Settings menu, then select **Plugins**.

## Installing a Plugin

1. On the **Plugins** page, click **Add Plugins**
2. Search the catalog of approved plugins and click **Install** on the one you want
3. Tendril downloads the package, resolves its dependencies, and loads the plugin

A progress bar is shown while the download runs. Once it finishes, the plugin appears in the installed list and anything it contributes (apps, menu items) becomes available immediately.

## Configuring a Plugin

Each installed plugin has an expandable row containing its settings. Most plugins need at least one value, such as an API key, before they can do anything.

A plugin that is missing a required value is marked **Unconfigured** and stays inactive until you provide one:

1. Expand the plugin
2. Fill in the fields (secrets are masked as you type)
3. Click **Save**

Saving reconfigures the plugin right away. Values are stored per plugin in `plugin-config.yaml`.

## Updating Plugins

Tendril periodically checks the catalog for newer versions of your installed plugins. When one is available, the plugin's row shows the new version number and an upward arrow.

- **Update** on the plugin's row updates that plugin
- **Update All** at the bottom of the page updates every plugin with a pending update

The downloaded package is verified against the catalog's recorded hash before it replaces the installed copy.

## Managing Installed Plugins

Each plugin row has these actions:

| Action | Effect |
|--------|--------|
| **Reload** | Unloads and reloads the plugin from disk, picking up any changes |
| **Unload** | Deactivates the plugin and removes its contributions. It moves to the **Unloaded Plugins** section, where **Load** brings it back |
| **Uninstall** | Removes the plugin. You can choose whether to keep its saved configuration |

Plugins installed from the catalog are deleted from disk when uninstalled. Local plugins are only removed from your references list, and their files are left in place.

Plugins that are present but not running are listed under **Unloaded Plugins**. If one failed to load, the reason is shown there (for example, requiring a newer version of Tendril) along with a **Retry** button.

## Installing Unapproved Plugins

The **Add Unapproved Plugin** menu at the bottom of the **Add Plugins** dialog installs plugins that are not in the catalog:

- **Add NuGet package** installs the latest version of a package by its package ID from nuget.org
- **Add local plugin** points Tendril at a folder on your machine

Local plugin folders are watched. If the folder contains source code, Tendril rebuilds and reloads the plugin whenever the files change, which is useful while developing one.

<Callout type="warning">
Unapproved plugins have not been reviewed, so only add plugins you trust. All plugins run inside the Tendril process with the same access to your machine and data that Tendril has.
</Callout>

## Where Plugins Live

Everything lives under `$TENDRIL_HOME/plugins/`. The **Open Plugins Folder** button on the Plugins page opens it in your file manager.

| Path | Contents |
|------|----------|
| `<PackageId>/` | An installed plugin package, one folder per plugin |
| `plugin-config.yaml` | Saved configuration values for each plugin, including secrets |
| `plugin-references.yaml` | Paths to local plugin folders |

## Building Your Own Plugin

See the [Plugin Developer Guide](https://github.com/Ivy-Interactive/Ivy-Tendril/blob/development/docs/plugin-developer-guide.md) for building, testing, and publishing a plugin.
