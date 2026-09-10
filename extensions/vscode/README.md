# Ivy Tendril for Visual Studio Code

<p align="center">
  <img src="resources/icon.png" alt="Ivy Tendril Logo" width="128" height="128" />
</p>

<p align="center">
  <strong>Autonomous plan management and agentic orchestration system inside Visual Studio Code.</strong>
</p>

<p align="center">
  <a href="https://marketplace.visualstudio.com/items?itemName=ivy-interactive.ivy-tendril"><img src="https://img.shields.io/visual-studio-marketplace/v/ivy-interactive.ivy-tendril?style=flat-square&label=Marketplace" alt="Visual Studio Marketplace Version" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-FSL--1.1--ALv2-blue?style=flat-square" alt="License" /></a>
  <img src="https://img.shields.io/badge/Pricing-Free-brightgreen?style=flat-square" alt="Pricing Free" />
</p>

---

## Overview

**Ivy Tendril** manages the entire autonomous software development pipeline from task intake to pull request merge:

**Task -> Plan -> Execution -> Verification -> PR -> Merge**

The Ivy Tendril extension brings the power of Tendril directly into your VS Code environment. Monitor autonomous coding agents in real time, review worktree changes with native VS Code diff editors, and trigger plan executions without leaving your editor.

---

## Features

### Embedded Webview Dashboard
Inspect plans, live execution graphs, job status, and logs directly inside an editor tab. Stay informed on autonomous agent progress as tasks move through drafting, execution, and verification.

### Activity Bar and Sidebar Container
Access Tendril from the dedicated Activity Bar icon. Monitor connection health to the Tendril daemon, check current server status, and quickly access active plans and isolated git worktrees.

### Server Lifecycle Management
Tendril automatically detects running server instances via the `.master` file located in your `TENDRIL_HOME` directory. If a server is not running, the extension can automatically spawn and supervise a background `tendril --web` daemon process.

### IDE Bridge Integration
Deep bi-directional integration between Tendril and VS Code:
- **File Navigation**: Jump directly to specific files and line numbers referenced in plans or agent logs.
- **Native Diff Inspection**: Open side-by-side git diffs comparing worktrees against the base branch.
- **Worktree Mounting**: Mount an isolated git worktree as a workspace folder with a single click.
- **Theme Synchronization**: Automatically synchronize light and dark UI themes between VS Code and the Tendril webview.

---

## Command Palette Reference

All commands are accessible from the VS Code Command Palette (`Cmd+Shift+P` on macOS, `Ctrl+Shift+P` on Windows/Linux):

| Command | Identifier | Description |
|---|---|---|
| Tendril: Open Dashboard | `tendril.openDashboard` | Opens the embedded Tendril dashboard in an editor tab. |
| Tendril: Open Worktree | `tendril.openWorktree` | Adds an active plan git worktree directory to your workspace. |
| Tendril: Start Server | `tendril.startServer` | Starts the local Tendril server daemon in the background. |
| Tendril: Stop Server | `tendril.stopServer` | Stops the running Tendril server daemon. |
| Tendril: Restart Server | `tendril.restartServer` | Restarts the Tendril server daemon. |

---

## Configuration Settings

Configure extension behavior via VS Code Settings (`settings.json`):

| Setting | Type | Default | Description |
|---|---|---|---|
| `tendril.executablePath` | `string` | `"tendril"` | Path to the `tendril` executable (defaults to `tendril` found on PATH). |
| `tendril.server.autoStart` | `boolean` | `true` | Automatically starts `tendril --web` when opening VS Code if no server is running. |
| `tendril.server.stopOnExit` | `boolean` | `false` | Stops the spawned Tendril server process when VS Code exits. |
| `tendril.server.port` | `number` | `0` | Preferred HTTP port for the spawned server (default `0` allows auto-assignment). |
| `tendril.server.pollTimeout` | `number` | `15000` | Milliseconds to wait for `.master` discovery after launching the daemon. |
| `tendril.homeDirectory` | `string` | `""` | Custom directory override for `TENDRIL_HOME` (defaults to environment or `~/.tendril`). |

---

## Prerequisites

- **Tendril CLI**: Installed and accessible in your system PATH (or specified via `tendril.executablePath`).
- **.NET 10 Runtime**: Required for running the Tendril server daemon and CLI tools.
- **Git**: Required for git worktree creation and repository management.

---

## Developer and Contributor Guide

### Building and Testing

Prerequisites: Node.js 20+ and npm.

```bash
# Navigate to extension directory
cd extensions/vscode

# Install dependencies
npm install

# Build the extension bundle (production mode via esbuild)
npm run build

# Run type checks
npm run typecheck

# Execute extension unit and integration tests
npm test
```

### Debugging with VS Code

The repository includes pre-configured launch and build tasks:
1. Open the `extensions/vscode` folder in VS Code.
2. Switch to the **Run and Debug** view (`Cmd+Shift+D` / `Ctrl+Shift+D`).
3. Select **Run Extension** and press **F5**. This compiles the extension in watch mode and launches a new Extension Development Host window.
4. To run tests interactively, select **Extension Tests** and press **F5**.

---

## License

This project is licensed under the Functional Source License, Version 1.1, Apache 2.0 Change License (FSL-1.1-ALv2). See the [LICENSE](LICENSE) file for complete terms.
