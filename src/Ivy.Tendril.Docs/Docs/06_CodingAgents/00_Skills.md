---
icon: Sparkles
searchHints:
  - skills
  - agent skills
  - plugins
  - copilot
  - claude
  - antigravity
  - cursor
  - codex
  - gemini
---

# Agent Skills

<Ingress>
Tendril Agent Skills package engineering, debugging, and review workflows for autonomous AI coding agents across Visual Studio Code, Claude Code, Antigravity, Cursor, OpenAI Codex, and Gemini CLI.
</Ingress>

## Overview

Agent skills adhere to the open agent skills specification. Each skill provides structured instructions, reference checklists, and automation scripts that guide coding agents through complex tasks:

- `tendril-debug-plan`: Deep-dives into plan logs, JSONL sessions, verification runs, and failure modes.
- `tendril-debug-job`: Analyzes raw agent execution artifacts and promptware logs.
- `tendril-review`: Performs thorough post-implementation code reviews, test gap analysis, and cleanup checks.
- `tendrillable`: Classifies GitHub issues for autonomous agent execution readiness.
- `tendril-release`: Automates package updates, versioning, pull requests, and deployment releases.
- `tendril-extension`: Builds, tests, packages, and links the Ivy Tendril extension into VS Code and Antigravity IDE.

## Universal Installation

Install skills for any supported agent using the universal skills CLI:

```bash
# Install all skills
npx skills add ivy-interactive/ivy-tendril

# Install an individual skill
npx skills add ivy-interactive/ivy-tendril --skill tendril-debug-plan
```

## Agent Integrations

### Visual Studio Code (GitHub Copilot & AI Extensions)

Install skills directly targeting GitHub Copilot in VS Code:

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot
```

Or install globally across all workspaces:

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot -g
```

Skills are stored in `.agents/skills/` (or `~/.copilot/skills/`) and appear in Copilot Chat under the `/skills` menu. You can also target companion extensions:
- Cline: `npx skills add ivy-interactive/ivy-tendril --agent cline`
- Continue: `npx skills add ivy-interactive/ivy-tendril --agent continue`
- Roo Code: `npx skills add ivy-interactive/ivy-tendril --agent roo`

For companion guide details, see [VS Code Setup](https://github.com/ivy-interactive/ivy-tendril/blob/development/docs/vscode-setup.md).

### Claude Code

Install via the Claude Code plugin marketplace:

```
/plugin marketplace add ivy-interactive/ivy-tendril
/plugin install tendril-skills@ivy-tendril
```

For local testing, start Claude Code pointing to your checkout:

```bash
claude --plugin-dir /path/to/ivy-tendril
```

For companion guide details, see [Claude Code Setup](https://github.com/ivy-interactive/ivy-tendril/blob/development/docs/claude-setup.md).

### Google Antigravity

Install using the Antigravity CLI:

```bash
agy plugin install https://github.com/ivy-interactive/ivy-tendril.git
```

Or from a local checkout:

```bash
agy plugin install ./
```

For companion guide details, see [Antigravity Setup](https://github.com/ivy-interactive/ivy-tendril/blob/development/docs/antigravity-setup.md).

### Cursor

Install targeting Cursor:

```bash
npx skills add ivy-interactive/ivy-tendril --agent cursor
```

Or place skills into `.cursor/skills/`. For companion guide details, see [Cursor Setup](https://github.com/ivy-interactive/ivy-tendril/blob/development/docs/cursor-setup.md).

### OpenAI Codex

Add the marketplace and install the plugin:

```bash
codex plugin marketplace add ivy-interactive/ivy-tendril
codex plugin add tendril-skills@tendril-skills
```

### Gemini CLI

Install skills directly using the Gemini CLI:

```bash
gemini skills install https://github.com/ivy-interactive/ivy-tendril.git --path skills
```
