---
icon: Gemini
searchHints:
  - gemini
  - google
  - coding agent
---

# Gemini CLI

<Ingress>
Gemini CLI is a coding agent powered by Google's Gemini models.
</Ingress>

## Configuration

Set Gemini as your coding agent in `config.yaml`:

```yaml
codingAgent: gemini
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Gemini CLI must be installed: `npm install -g @google/gemini-cli`
- Authenticate via `gemini auth` (browser-based OAuth) or set `GEMINI_API_KEY` environment variable

## Profiles

Tendril maps effort levels to Gemini models:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | gemini-2.5-pro | Complex multi-file changes |
| `balanced` | gemini-2.5-flash | Standard plan execution |
| `quick` | gemini-2.5-flash-lite | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.

## Available Models

- `gemini-2.5-pro` — Full reasoning, 1M context (default)
- `gemini-2.5-flash` — Fast and capable, 1M context
- `gemini-2.5-flash-lite` — Lightweight, lowest cost
- `gemini-3-pro-preview` — Next-gen reasoning (preview)
- `gemini-3-flash-preview` — Next-gen fast (preview)

Override the model in config:

```yaml
codingAgents:
  - name: gemini
    profiles:
      - name: deep
        model: gemini-3-pro-preview
```
