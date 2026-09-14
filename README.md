<h1>
  <a href="https://tendril.ivy.app"><img src="src/logo.png" alt="Tendril Logo" width="64" valign="middle" /></a> Ivy Tendril
</h1>

<p>
  <a href="https://github.com/Ivy-Interactive/Ivy-Tendril/stargazers"><img src="https://img.shields.io/github/stars/Ivy-Interactive/Ivy-Tendril?style=flat&label=%E2%98%85" alt="GitHub stars" /></a>
  <a href="https://github.com/Ivy-Interactive/Ivy-Tendril/releases/latest"><img src="https://img.shields.io/github/v/release/Ivy-Interactive/Ivy-Tendril?style=flat&label=release" alt="Latest Release" /></a>
  <a href="https://github.com/Ivy-Interactive/Ivy-Tendril/actions/workflows/repo-health.yml"><img src="https://img.shields.io/github/actions/workflow/status/Ivy-Interactive/Ivy-Tendril/repo-health.yml?branch=development&style=flat&label=CI" alt="CI Status" /></a>
  <a href="https://tendril.ivy.app"><img src="https://img.shields.io/badge/docs-tendril.ivy.app-blue?style=flat" alt="Documentation" /></a>
  <img src="https://img.shields.io/badge/macOS%20%7C%20Windows%20%7C%20Linux-4493F8?style=flat-square" alt="Supported platforms: macOS, Windows, and Linux" />
</p>

<h2>The Agentic Software Factory for 10x Builders</h2>

<p>
AI agents can now write 99% of the code. This changes what it means to be a developer. Our role shifts to knowing <strong>what good looks like</strong>. To do that, we need completely new developer tools. Tendril is that tool and replaces your IDE in an agentic era. 
</p>

<p>
<a href="https://youtu.be/_KVG1NnAj-8">
  <img src="docs/yt-thumbnail-in-two-minutes-2.png" alt="Ivy Tendril in two minutes: watch on YouTube" width="720">
</a>
</p>

<p>https://youtu.be/_KVG1NnAj-8</p>

## Features

<table>
<tr>
<td width="50%" valign="middle">

### Parallel Worktrees

Run agents in isolated git worktrees. Keep your main branch clean until you review, approve, and merge changes.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/worktrees.gif" alt="Parallel Worktrees" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Tunneling (Remote & Mobile Coding)

Expose your server securely using Cloudflare Quick Tunnels to monitor and steer agent runs from anywhere.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/tunneling.gif" alt="Tunneling" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Voice & Rich Input

Dictate prompts using built-in Whisper voice input and attach text files, logs, or documents with drag-and-drop.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/voice.gif" alt="Voice and Rich Input" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Plan Annotations

Annotate drafts inline to automatically update plans with revised agent goals.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/annotation.gif" alt="Plan Annotations" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Powerful Code Reviews

Review agent changes, inspect diffs, and approve code with automated verification gates.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/review.gif" alt="Making Code Reviews" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### GitHub Integration & Automated Inbox

Ingest GitHub Issues or jam.dev bug reports via webhooks to turn markdown plans into active jobs automatically.

[Docs &rarr;](https://tendril.ivy.app/docs/integrations/jamdev)

</td>
<td width="50%">
  <img src="src/github.gif" alt="GitHub Integration" width="100%" />
</td>
</tr>
</table>

---

## Supported Agents

Works with **any CLI agent**: if it runs in a terminal, it runs in Tendril.

<p>
  <a href="https://docs.anthropic.com/claude/docs/claude-code"><kbd><img src="https://www.google.com/s2/favicons?domain=anthropic.com&sz=64" alt="Claude Code logo" width="16" valign="middle" /> Claude Code</kbd></a> &nbsp;
  <a href="https://github.com/openai/codex"><kbd><img src="https://www.google.com/s2/favicons?domain=openai.com&sz=64" alt="Codex logo" width="16" valign="middle" /> Codex</kbd></a> &nbsp;
  <a href="https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli"><kbd><img src="https://www.google.com/s2/favicons?domain=github.com&sz=64" alt="GitHub Copilot logo" width="16" valign="middle" /> GitHub Copilot</kbd></a> &nbsp;
  <a href="https://gemini.google.com/cli"><kbd><img src="https://www.google.com/s2/favicons?domain=google.com&sz=64" alt="Gemini logo" width="16" valign="middle" /> Gemini</kbd></a> &nbsp;
  <a href="https://opencode.ai/docs/cli/"><kbd><img src="https://www.google.com/s2/favicons?domain=opencode.ai&sz=64" alt="OpenCode logo" width="16" valign="middle" /> OpenCode</kbd></a> &nbsp;
  <kbd>+ any CLI agent</kbd>
</p>
## Agent Skills

Extend your favorite AI coding agents with official Tendril engineering and debugging skills.

### Quick Start

Install Tendril skills for any supported agent using the universal skills installer:

```bash
npx skills add ivy-interactive/ivy-tendril
```

Or install a specific skill:

```bash
npx skills add ivy-interactive/ivy-tendril --skill tendril-debug-plan
```

### Supported Tools & Environments

<details>
<summary><strong>Visual Studio Code (GitHub Copilot & Extensions)</strong></summary>

Install skills for GitHub Copilot in VS Code:

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot
```

Global install (across all workspaces):

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot -g
```

Or copy skills directly to `.agents/skills/` or `.github/skills/` (project-level) or `~/.copilot/skills/` (global).

Once installed, skills appear in GitHub Copilot Chat under the `/skills` menu and can be invoked directly as slash commands (e.g. `/tendril-debug-plan`, `/tendril-debug-job`, `/tendril-review`, `/tendrillable`).

Third-party VS Code agent extensions:
- Cline: `npx skills add ivy-interactive/ivy-tendril --agent cline`
- Continue: `npx skills add ivy-interactive/ivy-tendril --agent continue`
- Roo Code: `npx skills add ivy-interactive/ivy-tendril --agent roo`

For full editor integration, install the official [Ivy Tendril VS Code Extension](https://marketplace.visualstudio.com/items?itemName=ivy-interactive.ivy-tendril) for embedded plan dashboards, worktree navigation, and live execution monitoring.

See the [VS Code Setup Guide](docs/vscode-setup.md) for detailed configuration options.
</details>

<details>
<summary><strong>Claude Code</strong></summary>

Install from the Claude Code marketplace:

```
/plugin marketplace add ivy-interactive/ivy-tendril
/plugin install tendril-skills@ivy-tendril
```

Local development:

```bash
claude --plugin-dir /path/to/ivy-tendril
```

See the [Claude Code Setup Guide](docs/claude-setup.md) for detailed configuration options.
</details>

<details>
<summary><strong>Antigravity CLI (agy)</strong></summary>

Install plugin via Git URL:

```bash
agy plugin install https://github.com/ivy-interactive/ivy-tendril.git
```

Local installation:

```bash
agy plugin install ./
```

See the [Antigravity Setup Guide](docs/antigravity-setup.md) for detailed configuration options.
</details>

<details>
<summary><strong>Cursor</strong></summary>

Install targeting Cursor:

```bash
npx skills add ivy-interactive/ivy-tendril --agent cursor
```

Or copy skills to `.cursor/skills/` (project-level) or `~/.cursor/skills/` (global).

See the [Cursor Setup Guide](docs/cursor-setup.md) for detailed configuration options.
</details>

<details>
<summary><strong>OpenAI Codex</strong></summary>

Install from the Codex plugin marketplace:

```bash
codex plugin marketplace add ivy-interactive/ivy-tendril
codex plugin add tendril-skills@tendril-skills
```
</details>

<details>
<summary><strong>Gemini CLI</strong></summary>

Install using the Gemini CLI:

```bash
gemini skills install https://github.com/ivy-interactive/ivy-tendril.git --path skills
```
</details>

---

## Install

Download standalone desktop installers (`.pkg`, `.AppImage`, `.exe`) directly from [GitHub Releases](https://github.com/Ivy-Interactive/Ivy-Tendril/releases/latest) or run one of the quick install commands below:

**macOS / Linux:**
```bash
curl -sSf https://cdn.ivy.app/install-tendril.sh | sh
```

**Windows:**
```powershell
irm https://cdn.ivy.app/install-tendril.ps1 | iex
```

### Run

Tendril is a desktop application, but can also be launched and controlled via the CLI:

Start the desktop application:
```bash
tendril
```

Start in headless mode (web server without desktop UI):
```bash
tendril --web
```

---

## Community & Support

- **Discord:** Join the community on **[Discord](https://discord.gg/FHgxkDga3y)**.
- **Feedback & Ideas:** Found a bug or have an idea? [Open an issue](https://github.com/Ivy-Interactive/Ivy-Tendril/issues).
- **Show Support:** [Star](https://github.com/Ivy-Interactive/Ivy-Tendril) this repo to follow along with our development.

---

## License

Tendril is source-available and licensed under the [Functional Source License (FSL-1.1-ALv2)](LICENSE).
