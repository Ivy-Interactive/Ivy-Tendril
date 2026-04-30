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

Or select it in **Settings > General > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Copilot CLI must be installed and available as `copilot` on your PATH
- An active GitHub Copilot subscription is required
- Run `copilot` once to complete authentication before using Tendril

## Profiles

Tendril maps effort levels to Copilot models:

| Profile | Model | Effort | Use Case |
|---------|-------|--------|----------|
| `deep` | gpt-5.2 | high | Complex multi-file changes |
| `balanced` | gpt-5.2 | medium | Standard plan execution |
| `quick` | gpt-5.2 | low | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.
