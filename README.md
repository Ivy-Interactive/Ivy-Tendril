<h1 align="center">
  <a href="https://tendril.ivy.app"><img src="src/logo.png" alt="Tendril Logo" width="64" valign="middle" /></a> Ivy Tendril
</h1>

<p align="center">
  <a href="https://github.com/Ivy-Interactive/Ivy-Tendril/stargazers"><img src="https://badgen.net/github/stars/Ivy-Interactive/Ivy-Tendril?label=%E2%98%85" alt="GitHub stars" /></a>
  <a href="https://www.nuget.org/packages/Ivy.Tendril"><img src="https://img.shields.io/nuget/v/Ivy.Tendril?style=flat" alt="NuGet version" /></a>
  <a href="https://www.nuget.org/packages/Ivy.Tendril"><img src="https://img.shields.io/nuget/dt/Ivy.Tendril?style=flat" alt="NuGet downloads" /></a>
  <a href="https://github.com/Ivy-Interactive/Ivy-Tendril/actions/workflows/publish-tendril.yml"><img src="https://img.shields.io/github/actions/workflow/status/Ivy-Interactive/Ivy-Tendril/publish-tendril.yml?style=flat&label=CI" alt="CI Status" /></a>
  <a href="https://tendril.ivy.app"><img src="https://img.shields.io/badge/docs-tendril.ivy.app-blue?style=flat" alt="Documentation" /></a>
  <img src="https://img.shields.io/badge/macOS%20%7C%20Windows%20%7C%20Linux-4493F8?style=flat-square" alt="Supported platforms: macOS, Windows, and Linux" />
</p>

<p align="center">
  <strong>The AI Orchestrator for 100x builders.</strong><br/>
  Orchestrate coding agents side-by-side, manage coding plans end-to-end, and track execution and costs in one place.
</p>

<p align="center">
  <img src="src/mockup.gif" alt="Tendril desktop app running agents and tracking jobs" width="960" />
</p>

## Features

<table>
<tr>
<td width="50%" valign="middle">

### Parallel Worktrees

Run agents in isolated git worktrees. Keep your main branch clean until you review, approve, and merge the changes.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="Parallel Worktrees" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Modular Promptwares

Deploy self-improving agents (CreatePlan, ExecutePlan, ExpandPlan, CreatePr) with their own prompts, tools, memory, and hooks.

[Docs &rarr;](https://tendril.ivy.app/docs/concepts/promptwares)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="Modular Promptwares" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Tunneling (Remote & Mobile Coding)

Expose your server securely using Cloudflare Quick Tunnels. Control, monitor, and steer your agent runs from anywhere, complete with a QR code in Settings for quick mobile access.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="Tunneling" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Voice & Rich Input

Dictate prompts using voice input (integrated Whisper WebSockets) and attach text files, logs, or project documents with drag-and-drop support.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="Voice and Rich Input" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### GitHub Integration & Automated Inbox

Watch your GitHub Issues or ingest bug reports from jam.dev via webhooks. The automated Inbox folder monitors markdown plans and turns them into active jobs.

[Docs &rarr;](https://tendril.ivy.app/docs/integrations/jamdev)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="GitHub Integration" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Verification Gates

Wire up build, test, lint, and format checks. Plans only advance when all checks pass, guaranteeing production-ready code.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/mockup.gif" alt="Verification Gates" width="100%" />
</td>
</tr>
</table>

**Also in the box:**

- **Chat with Agent (PTY):** Directly chat with running coding agents in a beta terminal-style split with system prompt injection.
- **Activity Heatmap:** View your 90-day PR contribution history on the wallpaper interface.
- **Plan Annotations:** Annotate drafts inline to automatically update plans with revised agent goals.
- **Rerun with Feedback:** Rerun plan steps with custom instructions to steer agents on failures.
- **Diagnostics & Testing:** Run one-click agent diagnostics to check installation, path, and model availability.
- **Plan state versioning:** Revert plan revisions, rename states, and migrate plan files with schema guards.

---

## Supported Agents

Works with **any CLI agent**: if it runs in a terminal, it runs in Tendril.

<p align="center">
  <a href="https://docs.anthropic.com/claude/docs/claude-code"><kbd><img src="https://www.google.com/s2/favicons?domain=anthropic.com&sz=64" alt="Claude Code logo" width="16" valign="middle" /> Claude Code</kbd></a> &nbsp;
  <a href="https://github.com/openai/codex"><kbd><img src="https://www.google.com/s2/favicons?domain=openai.com&sz=64" alt="Codex logo" width="16" valign="middle" /> Codex</kbd></a> &nbsp;
  <a href="https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli"><kbd><img src="https://www.google.com/s2/favicons?domain=github.com&sz=64" alt="GitHub Copilot logo" width="16" valign="middle" /> GitHub Copilot</kbd></a> &nbsp;
  <a href="https://gemini.google.com/cli"><kbd><img src="https://www.google.com/s2/favicons?domain=google.com&sz=64" alt="Gemini logo" width="16" valign="middle" /> Gemini</kbd></a> &nbsp;
  <a href="https://opencode.ai/docs/cli/"><kbd><img src="https://www.google.com/s2/favicons?domain=opencode.ai&sz=64" alt="OpenCode logo" width="16" valign="middle" /> OpenCode</kbd></a> &nbsp;
  <kbd>+ any CLI agent</kbd>
</p>

## Install

### One-Liner Install

Get up and running instantly with the standalone desktop app:

**macOS / Linux:**
```bash
curl -sSf https://cdn.ivy.app/install-tendril.sh | sh
```

**Windows:**
```powershell
irm https://cdn.ivy.app/install-tendril.ps1 | iex
```

### Run & Update

Start the Tendril server/application:
```bash
tendril
```

> **Tip:** The desktop app supports automated background self-updates. You can also rerun the installer command above at any time to upgrade to the latest release.

---

## Community & Support

- **Discord:** Join the community on **[Discord](https://discord.gg/FHgxkDga3y)**.
- **Feedback & Ideas:** Found a bug or have an idea? [Open an issue](https://github.com/Ivy-Interactive/Ivy-Tendril/issues).
- **Show Support:** [Star](https://github.com/Ivy-Interactive/Ivy-Tendril) this repo to follow along with our development.

---

## Developing

Want to contribute or run locally?

1. **Clone the repo:**
   ```bash
   git clone https://github.com/Ivy-Interactive/Ivy-Tendril.git
   cd Ivy-Tendril
   ```

2. **Run locally:**
   ```bash
   dotnet run --project src/Ivy.Tendril/Ivy.Tendril.csproj
   ```

See our [plugin developer guide](docs/plugin-developer-guide.md) to build custom integrations.

## License

Tendril is source-available and licensed under the [Functional Source License (FSL-1.1-ALv2)](LICENSE).
