---
icon: Terminal
searchHints:
  - promptware
  - agent
  - claude
  - prompt
  - tools
---

# Promptwares

<Ingress>
*Promptwares* are the self-improving agents behind each plan stage: each with its own prompt, tools, memory, and hooks.
</Ingress>

![Tendril Promptwares](https://i.postimg.cc/B6jK1sgB/promptware.gif)

They are located in a folder under `TENDRIL_HOME/Promptwares/`:

- **Program.md** — System prompt (goal and rules).
- **Tools/** — Scripts (e.g. PowerShell) the agent calls as tools.
- **Memory/** — Long-lived notes across runs.

Tendril runs them through the configured AI stack (e.g. Claude Code) for focused automation.

## Core Jobs

| Job | Role |
|-----|------|
| **CreatePlan** | Plan from a short brief or GitHub issue. |
| **ExecutePlan** | Worktree, implement, build, test. |
| **UpdatePlan** | Change existing code from review feedback. |
| **ExpandPlan** | Flesh out a thin plan. |
| **SplitPlan** | Split a large plan into sub-plans. |
| **CreatePr** | GitHub PR from the worktree diff (`gh`). |
| **CreateIssue** | Push plan failure/state to GitHub for triage. |

## Configuration

Each promptware is configured in `config.yaml` under the `promptwares:` key:

```yaml
promptwares:
  CreatePlan:
    profile: balanced
    allowedTools:
      - Read
      - Glob
      - Grep
      - Bash
      - Write(%PLANS_DIR%/**)
    customInstructions: |
      Always include acceptance criteria in the plan.
```

| Field | Required | Description |
|-------|----------|-------------|
| `profile` | Yes | Agent profile to use (`balanced`, `deep`, `quick`). |
| `allowedTools` | Yes | Tools the agent may call. Supports `%PLAN_FOLDER%` and `%PLANS_DIR%` variables. |
| `customInstructions` | No | Free-text instructions injected into the agent prompt. Takes precedence over Firmware and Program.md. |

A special `_default` entry applies as a baseline to all promptwares; specific entries override it.

### Custom Instructions

When `customInstructions` is set, Tendril appends them to the end of the Firmware prompt with an explicit priority marker. The agent is instructed to follow these over both the Firmware template and the promptware's `Program.md`. Use this for per-promptware behavioral overrides without editing the shared program files.

## Execution Flow

1. **Context** — Load `Program.md`; attach project context and custom instructions from `config.yaml`.
2. **Tools** — Expose `Tools/` via the tool protocol.
3. **Run** — Agent runs in the background with isolated state.
4. **Capture** — Stream to `logs/`; tokens and cost – `costs.csv`.

## Hooks (PowerShell)

- **pre_execute.ps1** — Setup, clone, env.
- **post_execute.ps1** — Cleanup, post-process.
- **on_error.ps1** — Errors, notify, telemetry.

Per-project customization is normal.