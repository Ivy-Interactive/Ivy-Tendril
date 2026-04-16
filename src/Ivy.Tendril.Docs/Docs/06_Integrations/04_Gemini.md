---
icon: Bot
searchHints:
  - gemini
  - google
  - coding agent
---

# Gemini

<Ingress>
Gemini is an alternative coding agent powered by Google's Gemini models.
</Ingress>

## Configuration

Set Gemini as your coding agent in `config.yaml`:

```yaml
codingAgent: gemini
```

Or select it in **Settings > General > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Profiles

Tendril maps effort levels to Gemini models:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | gemini-3-flash-preview | Complex multi-file changes |
| `balanced` | gemini-2.5-flash | Standard plan execution |
| `quick` | gemini-2.5-flash-lite | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level.
