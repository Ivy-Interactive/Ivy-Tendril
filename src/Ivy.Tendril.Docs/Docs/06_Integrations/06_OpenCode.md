---
icon: OpenCode
searchHints:
  - opencode
  - open code
  - coding agent
---

# OpenCode

<Ingress>
OpenCode is an alternative coding agent that supports multiple model providers through a unified CLI.
</Ingress>

## Configuration

Set OpenCode as your coding agent in `config.yaml`:

```yaml
codingAgent: opencode
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The OpenCode CLI must be installed and available as `opencode` on your PATH (`npm install -g opencode-ai`)
- Run `opencode providers login` to authenticate with your chosen model provider

## Profiles

Tendril maps effort levels to OpenCode models:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | default | Complex multi-file changes |
| `balanced` | default | Standard plan execution |
| `quick` | default | Simple fixes and small edits |

OpenCode supports multiple backend providers (Anthropic, OpenAI, Google, etc.) and manages model selection internally. Use `tendril models` to see discovered models.

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.
