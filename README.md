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
  <img src="src/main_newbg.gif" alt="Tendril desktop app running agents and tracking jobs" width="960" />
</p>

## Features

<table>
<tr>
<td width="50%" valign="middle">

### The Software Factory

The Tendril workflow behaves like a modern software factory. Visualize it as an assembly line where jobs flow systematically, starting at the Plan stage, going to Draft, and finally ending at the Review stage.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/factory_newbg.gif" alt="The Software Factory" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Chat with Agent

Directly chat with running coding agents in a terminal-style split with system prompt injection.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/agent_newbg.gif" alt="Chat with Agent" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Parallel Worktrees

Run agents in isolated git worktrees. Keep your main branch clean until you review, approve, and merge the changes.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/worktrees_newbg.gif" alt="Parallel Worktrees" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Advanced Git Management

Keep your repositories perfectly aligned using the SyncRepo dialog, which lets you synchronize branches, pull updates, and manage conflicts in a single view.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/gitsynch_newbg.gif" alt="Advanced Git Management" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Tunneling (Remote & Mobile Coding)

Expose your server securely using Cloudflare Quick Tunnels. Control, monitor, and steer your agent runs from anywhere, complete with a QR code in Settings for quick mobile access.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/tunneling_newbg.gif" alt="Tunneling" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Voice & Rich Input

Dictate prompts using voice input (integrated Whisper WebSockets) and attach text files, logs, or project documents with drag-and-drop support.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/voice_newbg.gif" alt="Voice and Rich Input" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Plan Annotations

Annotate drafts inline to automatically update plans with revised agent goals.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/annotation_newbg.gif" alt="Plan Annotations" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Powerful Code Reviews

Review and verify agent changes, inspect diffs, and approve code with verification gates.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
  <img src="src/codereview_newbg.gif" alt="Making Code Reviews" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### GitHub Integration & Automated Inbox

Watch your GitHub Issues or ingest bug reports from jam.dev via webhooks. The automated Inbox folder monitors markdown plans and turns them into active jobs.

[Docs &rarr;](https://tendril.ivy.app/docs/integrations/jamdev)

</td>
<td width="50%">
  <img src="src/ghimport_newbg.gif" alt="GitHub Integration" width="100%" />
</td>
</tr>
<tr>
<td width="50%" valign="middle">

### Multi-Interface Administration

Administer all operations in Tendril using the interface that fits your workflow. Configure and control jobs via the Command-Line Interface (CLI), Model Context Protocol (MCP) servers, or developer APIs.

[Docs &rarr;](https://tendril.ivy.app/docs/gettingstarted/introduction)

</td>
<td width="50%">
</td>
</tr>
</table>

**Also in the box:**

- **Modular Promptwares:** Deploy self-improving agents (CreatePlan, ExecutePlan, ExpandPlan, CreatePr) with their own prompts, tools, memory, and hooks.
- **Verification Gates:** Wire up build, test, lint, and format checks. Plans only advance when all checks pass, guaranteeing production-ready code.
- **Activity Heatmap:** View your 90-day PR contribution history on the wallpaper interface.
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
