---
icon: Construction
searchHints:
  - config
  - yaml
  - configuration
  - settings
  - projects
  - gui
---

# Setup & Settings

<Ingress>
Configure Tendril in the in-app **Settings** UI or by editing `TENDRIL_HOME/config.yaml` (projects, agents, levels, verifications, preferences).
</Ingress>

## Settings app

From Tendril, open setup without hand-editing YAML. Sections include:

- **General** — Default coding agent (`claude`, `codex`, `gemini`, `copilot`), max concurrent jobs, timeouts.
- **Levels** — Complexity tiers (e.g. L1–L3) and how agents weight large vs. small work.
- **Verifications** — Build / test / lint commands agents must satisfy.
- **Promptwares** — Paths to custom promptware folders and tools.
- **Projects** — Repos agents may clone and change.

## `config.yaml`

Same data lives in `TENDRIL_HOME/config.yaml`. Changes in the UI write here immediately.

### Example

```yaml
codingAgent: claude
maxConcurrentJobs: 3

projects:
  - name: MyProject
    color: Blue
    repos:
      - path: D:\Repos\MyProject
        prRule: default
    verifications:
      - name: Build
        required: true
      - name: Test
        required: true
      - name: CheckResult
        required: true
    meta:
      slackEmoji: ":rocket:"
```

### Common fields

| Field | Purpose |
|-------|---------|
| `codingAgent` | Agent runtime. See Claude Code, Codex, Gemini, or Copilot for details. |
| `maxConcurrentJobs` | Cap on parallel agent runs (worktrees). |
| `projects` | Registered repositories and their settings. |
| `api.apiKey` | Protect the REST API with a shared secret (see [REST API](../07_Advanced/02_REST.md)). |

## Verifications

Tendril ships with these built-in verification definitions. Wire them into project `verifications`:

| Name | Role |
|------|------|
| `Build` | Run the project's build command and verify zero errors |
| `Format` | Run the code formatter on changed files |
| `Test` | Run tests scoped by the plan's test section |
| `Lint` | Run the linter and fix any errors |
| `CheckResult` | Verify the implementation matches the plan |

Stack-specific verifications (e.g. `DotnetBuild`, `NpmTest`) can be added as custom entries in `config.yaml`.