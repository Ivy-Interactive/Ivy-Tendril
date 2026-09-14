# Visual Studio Code Setup Guide for Tendril Skills

This guide explains how to install, configure, and use Tendril Agent Skills with GitHub Copilot and other AI agent extensions in Visual Studio Code.

## 1. Quick Installation (Skills CLI)

The easiest way to install Tendril skills for GitHub Copilot in VS Code is using the open agent skills CLI:

```bash
# Project-level installation (installs into .agents/skills/ or .github/skills/)
npx skills add ivy-interactive/ivy-tendril --agent github-copilot

# Global installation (available across all VS Code workspaces)
npx skills add ivy-interactive/ivy-tendril --agent github-copilot -g
```

To install specific individual skills instead of the full package:

```bash
npx skills add ivy-interactive/ivy-tendril --skill tendril-debug-plan --agent github-copilot
```

## 2. Manual Installation Paths

If you prefer placing skill folders manually without the CLI:

- **Workspace repository (recommended for teams)**:
  Copy skills into `.agents/skills/<skill-name>` or `.github/skills/<skill-name>` at your workspace root.
- **User profile (global for all projects)**:
  Copy skills into `~/.copilot/skills/<skill-name>` (macOS/Linux) or `%USERPROFILE%\.copilot\skills\<skill-name>` (Windows).

Ensure each skill folder contains its `SKILL.md` specification and any accompanying `references/` or `scripts/` directories.

## 3. Using Skills in GitHub Copilot Chat

Once installed, GitHub Copilot automatically discovers the skills:

1. Open Copilot Chat in VS Code (`Ctrl+Alt+I` / `Cmd+Ctrl+I`).
2. Type `/skills` to inspect loaded skills and descriptions.
3. Invoke any Tendril skill directly as a command:
   - `/tendril-debug-plan <plan-id>`: Inspect execution logs, timeline, and verifications for a plan.
   - `/tendril-debug-job <job-id>`: Analyze job artifacts, agent logs, and raw events.
   - `/tendril-review`: Run a comprehensive post-change code and test review on modified files.
   - `/tendrillable <url>`: Evaluate GitHub issues for autonomous agent execution.

## 4. Integration with Other VS Code AI Extensions

Tendril skills adhere to the open agent skills standard and work seamlessly with third-party VS Code extensions:

### Cline
```bash
npx skills add ivy-interactive/ivy-tendril --agent cline
```
Skills are written to `.cline/skills/` or the global Cline configuration directory.

### Continue
```bash
npx skills add ivy-interactive/ivy-tendril --agent continue
```
Skills are installed into your `.continue/skills/` directory and can be referenced in prompt context.

### Roo Code (Roo Clinic)
```bash
npx skills add ivy-interactive/ivy-tendril --agent roo
```
Installed into `.roo/skills/` for custom system modes and task execution.

## 5. Pairing with the Official Ivy Tendril VS Code Extension

For an integrated development workflow, install the official [Ivy Tendril VS Code Extension](https://marketplace.visualstudio.com/items?itemName=ivy-interactive.ivy-tendril):

- **Plan Dashboard**: Browse, review, and trigger plans directly from the sidebar.
- **Worktree Navigator**: Jump into isolated execution worktrees with a single click.
- **Server Control**: Start, stop, and inspect background Tendril server processes.

Pairing Tendril skills with the VS Code extension gives you a complete control center for autonomous coding agent orchestration.
