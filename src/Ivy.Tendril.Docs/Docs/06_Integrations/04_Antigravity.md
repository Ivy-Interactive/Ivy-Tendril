---
icon: Antigravity
searchHints:
  - antigravity
  - agy
  - coding agent
---

# Antigravity

<Ingress>
Antigravity is an alternative coding agent powered by the Antigravity CLI.
</Ingress>

## Configuration

Set Antigravity as your coding agent in `config.yaml`:

```yaml
codingAgent: antigravity
```

Or select it in **Settings > Coding Agent**.

For more details on `config.yaml` structure and settings, see [Setup & Settings](../03_Configuration/01_Setup.md).

## Requirements

- The Antigravity CLI must be installed and available as `agy` on your PATH
- Run `agy` once to complete the browser-based authentication flow

## Profiles

Tendril maps effort levels to Antigravity profiles:

| Profile | Model | Use Case |
|---------|-------|----------|
| `deep` | default | Complex multi-file changes |
| `balanced` | default | Standard plan execution |
| `quick` | default | Simple fixes and small edits |

The profile is selected automatically based on the plan's complexity level, or can be configured per promptware in `config.yaml`.
