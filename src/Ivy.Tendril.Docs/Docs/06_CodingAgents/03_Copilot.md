---
icon: Github
searchHints:
  - copilot
  - github
  - coding agent
---

# Copilot

<Ingress>
Copilot is an alternative coding agent powered by GitHub's Copilot CLI.
</Ingress>

## Configuration

Set Copilot as your coding agent in `config.yaml`:

```yaml
codingAgent: copilot
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Copilot CLI must be available as `copilot` on your PATH, or as `gh copilot` via the GitHub CLI
- An active GitHub Copilot subscription is required
- Authenticate via `gh auth login` before using Tendril

## Profiles

Tendril maps effort levels to Copilot:

| Profile | Model | Effort | Use Case |
|---------|-------|--------|----------|
| `deep` | gpt-5.4 | high | Complex multi-file changes |
| `balanced` | gpt-5.4 | medium | Standard plan execution |
| `quick` | gpt-5.4 | low | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.

## Installing Tendril Skills for GitHub Copilot

Tendril provides specialized skills for GitHub Copilot in Visual Studio Code, covering plan debugging, job artifact inspection, code reviews, and issue triage.

### Using the Skills CLI

Install skills for your workspace:

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot
```

Or install globally across all workspaces:

```bash
npx skills add ivy-interactive/ivy-tendril --agent github-copilot -g
```

### Manual Placement in `.agents/skills/`

Skills can also be placed directly in the `.agents/skills/` or `.github/skills/` directory at your repository root:

```bash
mkdir -p .agents/skills
cp -r /path/to/skills/* .agents/skills/
```

Once installed, skills appear in GitHub Copilot Chat under the `/skills` menu and can be invoked directly as slash commands (e.g. `/tendril-debug-plan`, `/tendril-review`).

For more details, see [Agent Skills](00_Skills.md).

