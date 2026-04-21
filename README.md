![Tendril Logo](src/Tendril.svg)

[![NuGet](https://img.shields.io/nuget/v/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Ivy.Tendril?style=flat)](https://www.nuget.org/packages/Ivy.Tendril)
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

## Installation

### Quick Install

One-liner: installs Tendril and required backend tools.

**macOS / Linux**

```bash
curl -sSf https://raw.githubusercontent.com/Ivy-Interactive/Ivy-Tendril/main/src/install.sh | sh
```

**Windows**

```powershell
Invoke-RestMethod -Uri https://raw.githubusercontent.com/Ivy-Interactive/Ivy-Tendril/main/src/install.ps1 | Invoke-Expression
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

- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) (`claude`), [Codex CLI](https://github.com/openai/codex) (`codex`), or [Gemini CLI](https://github.com/google-gemini/gemini-cli) (`gemini`)
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
