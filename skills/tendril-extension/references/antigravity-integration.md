# Antigravity IDE Integration Guide for Ivy Tendril

## Architecture Overview

Antigravity IDE is built on top of the VS Code open-source platform (Code OSS) with custom AI and agentic capabilities.

### Extension Storage
- Antigravity IDE stores its user extensions in `~/.antigravity-ide/extensions`.
- Extensions placed or symlinked directly into this folder are automatically recognized by the extension host.
- Each extension directory typically follows the format `<publisher>.<extension-name>-<version>`.

### Engine Compatibility
- Antigravity IDE currently reports engine version `1.107.0`.
- The CLI command `antigravity-ide --install-extension <file.vsix>` performs strict semver validation against the `engines.vscode` field in `package.json`.
- Direct symlinking into `~/.antigravity-ide/extensions` bypasses runtime CLI installer rejections while allowing full functionality in the IDE.

### Available Tendril Commands
- `tendril.openDashboard`: Opens the Tendril web dashboard in the browser or webview.
- `tendril.openWorktree`: Quickly selects and opens an isolated plan execution worktree.
- `tendril.startServer`: Launches the background Tendril master process (`tendril --web`).
- `tendril.stopServer`: Terminates the background Tendril server.
- `tendril.restartServer`: Restarts the Tendril server.
