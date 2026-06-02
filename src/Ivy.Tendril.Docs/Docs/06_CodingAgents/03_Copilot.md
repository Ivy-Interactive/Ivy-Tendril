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
