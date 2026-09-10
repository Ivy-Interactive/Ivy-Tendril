# Ivy Tendril VS Code Extension

Visual Studio Code integration for Ivy Tendril, an autonomous plan management and agentic orchestration system.

## Features

- **Embedded Dashboard**: View Tendril's full web interface directly inside an editor tab.
- **Activity Bar Sidebar**: Monitor Tendril server status, run quick actions, and inspect recent plans.
- **Status Bar Integration**: Check connection status and running job count at a glance.
- **Server Lifecycle Management**: Automatically discover running server instances via `~/.tendril/.master`, or spawn and manage a local `tendril --web` daemon process.
- **Bi-directional IDE Bridge**: Seamlessly open files at specific line numbers, open git worktrees as workspace folders, view native side-by-side diffs, and synchronize color themes.

## Commands

- `tendril.openDashboard`: Open the embedded Tendril dashboard in an editor tab.
- `tendril.openWorktree`: Add a worktree directory to the active VS Code workspace.
- `tendril.startServer`: Start the local Tendril server daemon.
- `tendril.stopServer`: Stop the spawned Tendril server daemon.
- `tendril.restartServer`: Restart the Tendril server daemon.

## Configuration

- `tendril.executablePath`: Path to the `tendril` executable (defaults to `tendril` on PATH).
- `tendril.server.autoStart`: Automatically start `tendril --web` when opening VS Code if not running (default: `true`).
- `tendril.server.stopOnExit`: Stop the spawned server when closing VS Code (default: `false`).
- `tendril.server.port`: Preferred port for the spawned server (default: `0` for auto-assign).
- `tendril.server.pollTimeout`: Milliseconds to wait for `.master` creation upon server startup (default: `15000`).
- `tendril.homeDirectory`: Custom `TENDRIL_HOME` directory path (defaults to environment variable or `~/.tendril`).

## Development

```bash
# Install dependencies
npm install

# Build extension and tests
npm run build

# Type check
npm run typecheck

# Run tests
npm test
```
