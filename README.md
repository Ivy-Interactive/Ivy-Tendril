![Tendril Logo](src/Tendril.svg)

[![NuGet](https://img.shields.io/nuget/v/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
[![CI](https://img.shields.io/github/actions/workflow/status/Ivy-Interactive/Ivy-Tendril/publish-tendril.yml?style=flat&label=CI)](https://github.com/Ivy-Interactive/Ivy-Tendril/actions/workflows/publish-tendril.yml)
[![Documentation](https://img.shields.io/badge/docs-tendril.ivy.app-blue?style=flat)](https://tendril.ivy.app)

# Ivy Tendril - AI Coding Agent Orchestrator

![Tendril UI](src/mockup.gif)

Tendril is a web application built on [Ivy Framework](https://github.com/Ivy-Interactive/Ivy-Framework) that manages AI coding plans end-to-end. It orchestrates coding agents (Claude, Codex, Copilot, Gemini, OpenCode) through a structured lifecycle — from plan creation and expansion to execution, verification, and PR generation. Tendril tracks jobs, costs, tokens, and verification results, giving you full visibility into your AI-assisted development workflow.

For complete documentation, configuration options, and guides, visit the Tendril documentation at [tendril.ivy.app](https://tendril.ivy.app).

## Features

- **Plan lifecycle management** — Draft, Execute, Review, and PR stages with state tracking
- **Multi-agent support** — Orchestrate Claude, Codex, Copilot, Gemini, and OpenCode with configurable profiles (deep, balanced, quick)
- **Multi-project support** — Configure multiple repos with per-project verifications
- **Job monitoring** — Live cost and token tracking for running agents
- **Dashboard** — Activity statistics and plan counts at a glance
- **GitHub PR integration** — Automated pull request creation from completed plans
- **Plan review workflow** — Review diffs, run sample apps, approve or send back for revision

## Installation

### Quick Install

One-liner: installs Tendril and required backend tools.

**macOS / Linux**

```bash
curl -sSf https://cdn.ivy.app/install-tendril.sh | sh
```

**Windows**

```powershell
irm https://cdn.ivy.app/install-tendril.ps1 | iex
```

### .NET Tool

Global install from NuGet:

```bash
dotnet tool install -g Ivy.Tendril
```

> **Tip:** PowerShell 7, Git and gh CLI need to be present on your machine if you install using the `dotnet tool` command.

### Run

```bash
tendril
```

### Update

```bash
dotnet tool update -g Ivy.Tendril
```

## Development

### Prerequisites

- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) (`claude`), [Codex CLI](https://github.com/openai/codex) (`codex`), [Gemini CLI](https://github.com/google-gemini/gemini-cli) (`gemini`), [Copilot CLI](https://github.com/microsoft/copilot-cli) (`copilot`), or [OpenCode](https://github.com/nicepkg/opencode) (`opencode`)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [GitHub CLI](https://cli.github.com/) (`gh`)
- [PowerShell 7+](https://github.com/PowerShell/PowerShell)
- [Git](https://git-scm.com/)

### Setup

1. **Clone the repo**

   ```bash
   git clone https://github.com/Ivy-Interactive/Ivy-Tendril.git
   cd Ivy-Tendril
   ```

2. **Run**

    ```bash
    dotnet run --project src/Ivy.Tendril/Ivy.Tendril.csproj
    ```

## License

Tendril is licensed under the [Functional Source License (FSL-1.1-ALv2)](LICENSE). This means the source is available and you can use it for most purposes, but competing products are not permitted. After two years, each release converts to Apache 2.0.
