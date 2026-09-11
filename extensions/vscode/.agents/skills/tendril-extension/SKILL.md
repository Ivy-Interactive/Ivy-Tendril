---
name: tendril-extension
description: >-
  Manage, build, test, package, install, and debug the Ivy Tendril extension in Antigravity IDE and VS Code.
  Use when the user asks to install, link, update, build, test, or package the Ivy Tendril extension for Antigravity IDE.
---

# Ivy Tendril Extension Skill

This skill provides procedures and helper scripts for building, testing, packaging, and installing the Ivy Tendril extension directly into **Antigravity IDE** or VS Code.

## Directory & Environment

- **Extension Directory**: `/Users/rorychatt/git/ivy-tendril/extensions/vscode`
- **Antigravity Extensions Directory**: `~/.antigravity-ide/extensions`
- **Installed Extension Name**: `ivy-interactive.ivy-tendril-0.1.0`
- **Package Manager**: `pnpm` (fixed versions configured via `.npmrc`)

---

## Common Workflows

### 1. Install / Link to Antigravity IDE

To compile the latest extension bundle and link it live into Antigravity IDE:

```bash
/Users/rorychatt/git/ivy-tendril/extensions/vscode/.agents/skills/tendril-extension/scripts/install-antigravity.sh
```

Or manually:

```bash
cd /Users/rorychatt/git/ivy-tendril/extensions/vscode
pnpm install
pnpm run build
mkdir -p ~/.antigravity-ide/extensions
ln -sfn /Users/rorychatt/git/ivy-tendril/extensions/vscode ~/.antigravity-ide/extensions/ivy-interactive.ivy-tendril-0.1.0
antigravity-ide --list-extensions | grep "ivy-interactive.ivy-tendril"
```

After installing/linking:
1. Reload Antigravity IDE by opening the Command Palette (`Cmd+Shift+P`) and selecting **Developer: Reload Window**.
2. The Tendril sidebar view and commands (`Tendril: Open Dashboard`, `Tendril: Start Server`, etc.) are now active.

---

### 2. Package as `.vsix` Archive

To build a standalone VSIX distribution file:

```bash
/Users/rorychatt/git/ivy-tendril/extensions/vscode/.agents/skills/tendril-extension/scripts/package-vsix.sh
```

Or manually:

```bash
cd /Users/rorychatt/git/ivy-tendril/extensions/vscode
pnpm install
pnpm run build
npx @vscode/vsce package --no-dependencies
```

> [!NOTE]
> Since Antigravity IDE runs VS Code engine `1.107.0`, installing a VSIX with an engine requirement greater than `1.107.0` via `antigravity-ide --install-extension` will be rejected by the version validator. The direct symlink method (Workflow 1) is the recommended approach for local development and daily usage.

---

### 3. Run Extension Tests & Typecheck

To verify extension integrity and TypeScript compliance:

```bash
cd /Users/rorychatt/git/ivy-tendril/extensions/vscode
pnpm run typecheck
pnpm test
```

---

### 4. Uninstall / Unlink from Antigravity IDE

To remove the extension from Antigravity IDE:

```bash
/Users/rorychatt/git/ivy-tendril/extensions/vscode/.agents/skills/tendril-extension/scripts/uninstall-antigravity.sh
```

Or manually:

```bash
rm -rf ~/.antigravity-ide/extensions/ivy-interactive.ivy-tendril-0.1.0
```
