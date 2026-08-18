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

| Profile | Model | Effort | Use Case |
|---------|-------|--------|----------|
| `deep` | opus | max | Complex multi-file changes, architecture work |
| `balanced` | sonnet | high | Standard plan execution, most tasks |
| `quick` | haiku | low | Simple fixes, formatting, small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.

## Available models

| Model | ID | Context Window | Pricing (input / output per MTok) |
|-------|----|-----------------|------------------------------------|
| Claude Fable | `claude-fable-5` | 1M | $10.00 / $50.00 |
| Claude Opus 5 | `claude-opus-5` | 1M | $5.00 / $25.00 |
| Claude Opus | `opus` | 1M | $5.00 / $25.00 |
| Claude Sonnet | `sonnet` | 1M | $2.00 / $10.00 |
| Claude Haiku | `haiku` | 200k | $1.00 / $5.00 |

`opus`, `sonnet`, and `haiku` are Claude Code aliases that track Anthropic's current model for that tier, while `claude-opus-5` and `claude-fable-5` are pinned IDs.

Claude Sonnet's introductory pricing of $2.00 / $10.00 applies through 2026-08-31; standard pricing of $3.00 / $15.00 applies after.
