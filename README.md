![Tendril Logo](src/Tendril.svg)

[![NuGet](https://img.shields.io/nuget/v/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
[![License](https://img.shields.io/github/license/Ivy-Interactive/Ivy-Tendril?style=flat)](LICENSE)
[![CI](https://img.shields.io/github/actions/workflow/status/Ivy-Interactive/Ivy-Tendril/publish-tendril.yml?style=flat&label=CI)](https://github.com/Ivy-Interactive/Ivy-Tendril/actions/workflows/publish-tendril.yml)
[![website](https://img.shields.io/badge/website-ivy.app-green?style=flat)](https://tendril.ivy.app)

# Ivy Tendril - AI Coding Agent Orchestrator

Tendril is a web application built on [Ivy Framework](https://github.com/Ivy-Interactive/Ivy-Framework) that manages AI coding plans end-to-end. It orchestrates coding agents (Claude, Codex, Gemini) through a structured lifecycle — from plan creation and expansion to execution, verification, and PR generation. Tendril tracks jobs, costs, tokens, and verification results, giving you full visibility into your AI-assisted development workflow.

## Features

- **Plan lifecycle management** — Draft, Execute, Review, and PR stages with state tracking
- **Multi-agent support** — Orchestrate Claude, Codex, and Gemini with configurable profiles (deep, balanced, quick)
- **Multi-project support** — Configure multiple repos with per-project verifications
- **Job monitoring** — Live cost and token tracking for running agents
- **Dashboard** — Activity statistics and plan counts at a glance
- **GitHub PR integration** — Automated pull request creation from completed plans
- **Plan review workflow** — Review diffs, run sample apps, approve or send back for revision

## Prerequisites

### For Running Tendril

- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) (`claude`), [Codex CLI](https://github.com/openai/codex) (`codex`), or [Gemini CLI](https://github.com/google-gemini/gemini-cli) (`gemini`)
- [GitHub CLI](https://cli.github.com/) (`gh`)
- PowerShell
- Git

### For Development

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

## Setup

1. **Clone the repo**

   ```bash
   git clone https://github.com/Ivy-Interactive/Ivy-Tendril.git
   cd Ivy-Tendril
   ```

2. **Configure `config.yaml`**

   Copy the example config and edit it:

   ```bash
   cp src/Ivy.Tendril/example.config.yaml ~/.tendril/config.yaml
   ```

   Key fields:
   - `projects` — List of projects with their repo paths, verifications, and context
   - `codingAgent` — The coding agent to use (`claude`, `codex`, or `gemini`)

3. **Set `TENDRIL_HOME` environment variable**

   Point `TENDRIL_HOME` to your Tendril data directory:

   ```bash
   export TENDRIL_HOME=~/.tendril
   mkdir -p "$TENDRIL_HOME"
   ```

   Tendril will populate this with `Plans/`, `Inbox/`, `Trash/`, and `config.yaml` at runtime. If `TENDRIL_HOME` is not set, Tendril will launch the onboarding wizard.

4. **Run**

    ```bash
    dotnet run --project src/Ivy.Tendril/Ivy.Tendril.csproj
    ```

### Installing as Global CLI Tool (NPM)

You can run Tendril from any directory using `npx` or by installing it globally via `npm`.

1. **Via `npx`**

   ```bash
   npx @ivy/tendril
   ```

2. **Global Install**

   ```bash
   npm install -g @ivy/tendril
   dotnet tool install -g Ivy.Tendril
   tendril
   ```

   *(Note: The NPM package is a wrapper for the `dotnet tool`. Both must be available for the `tendril` command to work.)*
