---
icon: ClaudeCode
searchHints:
  - claude
  - claude code
  - anthropic
  - coding agent
  - ai agent
---

# Claude Code

<Ingress>
Claude Code is the default coding agent in Tendril, powered by Anthropic's Claude models.
</Ingress>

## Configuration

Set Claude Code as your coding agent in `config.yaml`:

```yaml
codingAgent: claude
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Claude CLI must be installed and available as `claude` on your PATH (`npm install -g @anthropic-ai/claude-code`)
- Run `claude login` to authenticate before using Tendril

## Profiles

Tendril maps effort levels to Claude models:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | opus | Complex multi-file changes, architecture work |
| `balanced` | sonnet | Standard plan execution, most tasks |
| `quick` | haiku | Simple fixes, formatting, small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.
