---
icon: Construction
searchHints:
  - config
  - yaml
  - configuration
  - settings
  - projects
  - gui
  - deployment
  - docker
  - secrets
  - BasicAuth
  - password
  - hosted
---

# Setup & Settings

<Ingress>
Configure Tendril in the in-app **Settings** UI or by editing `TENDRIL_HOME/config.yaml` (projects, agents, levels, verifications, preferences).
</Ingress>

## Settings app

From Tendril, open setup without hand-editing YAML. Sections include:

- **General** — Default coding agent (`claude`, `codex`, `antigravity`, `copilot`, `opencode`), max concurrent jobs, timeouts.
- **Levels** — Complexity tiers (e.g. L1–L3) and how agents weight large vs. small work.
- **Verifications** — Build / test / lint commands agents must satisfy.
- **Promptwares** — Paths to custom promptware folders and tools.
- **Projects** — Repos agents may clone and change.

## `config.yaml`

Same data lives in `$TENDRIL_HOME/config.yaml` (default: `~/.tendril/config.yaml`). Changes in the UI write here immediately.

**Note:** The configuration file must be named `config.yaml` (not `tendril-config.yaml`). The `TENDRIL_CONFIG` environment variable points to this file's full path.

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
| `codingAgent` | Agent runtime. See Claude Code, Codex, Antigravity, or Copilot for details. |
| `maxConcurrentJobs` | Cap on parallel agent runs (worktrees). |
| `projects` | Registered repositories and their settings. |
| `api.apiKey` | Protect the REST API with a shared secret (see [REST API](../08_Advanced/02_REST.md)). |

## Password on a server (env secrets)

Set **`TENDRIL_HOME`** to your data directory. For a password on the web UI, set these env vars (double underscore is normal on hosts like Sliplane):

**`BasicAuth__Users`** — login in plain text, e.g. `you:your-password`. No colon = password only.

**`BasicAuth__HashSecret`** — one random **base64** line (`openssl rand -base64 32`). Used to hash the password.

**`BasicAuth__JwtSecret`** — same base64 as `BasicAuth__HashSecret` if your host shows three secrets and you want to fill all three. Otherwise optional: it is a second **name** for the same value, not a second secret.

You can use `TENDRIL_AUTH_PASSWORD`, `TENDRIL_AUTH_USERNAME`, and `TENDRIL_AUTH_HASH_SECRET` instead of the `BasicAuth__*` names if you prefer.

REST uses **`api.apiKey`** in config, not these vars — see [REST API](../08_Advanced/02_REST.md). **Settings** in the app still edits `config.yaml` after startup.

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