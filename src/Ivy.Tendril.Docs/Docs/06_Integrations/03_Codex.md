---
icon: OpenAI
searchHints:
  - codex
  - openai
  - gpt
  - coding agent
---

# Codex

<Ingress>
Codex is an alternative coding agent powered by OpenAI's GPT models.
</Ingress>

## Configuration

Set Codex as your coding agent in `config.yaml`:

```yaml
codingAgent: codex
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Codex CLI must be installed and available as `codex` on your PATH
- Run `codex login` to authenticate before using Tendril

## Profiles

Tendril maps effort levels to Codex models:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | gpt-5.4 | Complex multi-file changes |
| `balanced` | gpt-5.4-mini | Standard plan execution |
| `quick` | gpt-5.3-codex | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.
