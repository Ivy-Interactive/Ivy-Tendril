# Tendril

Tendril is a plan management and agentic orchestration system. It manages a pipeline from task intake through autonomous execution:

**Task → Plan → Execution → Verification → PR → Merge**

You are an interactive assistant for the human operator. Users open this session to create plans, debug failures, inspect plan state, work on the Tendril codebase, or ask questions about the system.

## Environment

- **TENDRIL_HOME**: `D:/Tendril`
- **Plans folder**: `D:/Plans`
- **Config**: `D:/Tendril/config.yaml`
- **Database**: `D:/Tendril/tendril.db`

```
D:/Tendril/
  config.yaml          # Projects, agents, verifications, promptware settings
  tendril.db           # SQLite database (plan state, jobs, costs)
  Plans/               # Plan folders ({ID}-{Title}/)
  Promptwares/         # Deployed promptware programs
  Logs/Jobs/           # Failed job output
```

## Plan Lifecycle

Plans move through these states:

| State | Meaning |
|-------|---------|
| `Draft` | Ready for review or action by the user |
| `Building` | CreatePlan or ExpandPlan agent working |
| `Updating` | UpdatePlan or SplitPlan agent refining |
| `Executing` | ExecutePlan agent implementing in a worktree |
| `ReadyForReview` | Execution complete, awaiting human review |
| `Failed` | Agent errored or verifications consistently failed |
| `Completed` | PR created and merged |
| `Skipped` | Dismissed or split into child plans |
| `Blocked` | Waiting for dependency plans to complete |
| `Icebox` | Parked for later |

**Transitions:**

```
CreatePlan ──► Draft
               ├─ ExpandPlan ──► Building ──► Draft
               ├─ UpdatePlan ──► Updating ──► Draft
               ├─ SplitPlan  ──► Updating ──► Skipped (original) + new Drafts
               ├─ ExecutePlan ──► Executing ──► ReadyForReview or Failed
               ├─ CreatePr (from Review) ──► Completed
               ├─ (manual) ──► Skipped / Icebox
               └─ (dependencies unmet) ──► Blocked ──► Draft (when unblocked)
```

**Key rules:**
- `dependsOn` blocks execution until all dependencies are Completed AND their PRs merged
- Verifications (Build, Test, Format, CheckResult) gate progress from Executing to ReadyForReview
- Plans execute in isolated git worktrees, never in the original repos

## Promptwares

Autonomous agents that handle each pipeline stage. Each has a `Program.md` (instructions), `Tools/` (scripts), and `Memory/` (persistent learnings).

| Promptware | What it does |
|------------|-------------|
| **CreatePlan** | Researches codebase, detects duplicates, writes implementation plan |
| **ExpandPlan** | Transforms vague/investigative plans into concrete implementation steps |
| **UpdatePlan** | Incorporates user feedback, answers questions, writes new revision |
| **SplitPlan** | Breaks multi-issue plans into separate self-contained plans |
| **ExecutePlan** | Implements plan in git worktree, runs verifications, generates summary |
| **RetryPlan** | Applies reviewer feedback to an already-executed plan's worktree |
| **CreatePr** | Pushes branches, creates GitHub PRs, applies merge rules |
| **CreateIssue** | Creates GitHub issues from plans |
| **UpdateProject** | Sets up project verifications and review actions |

## Plan Structure

Plans live in `D:/Plans/{ID}-{SafeTitle}/`:

```
00142-FixLoginBug/
  plan.yaml              # Metadata (use CLI only, never edit directly)
  Revisions/             # 001.md, 002.md, ... (plan content)
  Verification/          # DotnetBuild.md, DotnetTest.md, ...
  Worktrees/             # Isolated git checkouts for execution
  Artifacts/             # summary.md, screenshots/, tests/
```

**plan.yaml key fields:** state, project, level, title, repos, verifications, dependsOn, relatedPlans, commits, prs, executionProfile, sourceUrl

**Revision format:**

```markdown
# Title

## Problem
What needs to be fixed or built

## Solution
Technical approach with file paths and steps

## Tests
New tests to write + test scope filter

## Verification
- [x] DotnetBuild
- [x] DotnetTest
```

## Tendril CLI Reference

The `tendril` CLI manages plans, projects, verifications, and system state.

Plan IDs accept: full path, folder name, zero-padded ID (e.g., `00015`), or bare number (e.g., `15`).

### Root Commands

| Command | Description |
|---------|-------------|
| `tendril doctor` | Check system health |
| `tendril version` | Show version |
| `tendril update` | Update Tendril |
| `tendril update-promptwares` | Update promptware programs |
| `tendril models` | List available models and pricing |

### Plan Commands

| Command | Description |
|---------|-------------|
| `tendril plan list` | List plans (supports filters) |
| `tendril plan create <title>` | Create a new plan |
| `tendril plan update <plan-id>` | Update plan from stdin |
| `tendril plan set <plan-id> <field> <value>` | Set a plan field |
| `tendril plan get <plan-id> [field]` | Get plan data |
| `tendril plan validate <plan-id>` | Validate plan health |
| `tendril plan doctor` | Check all plans health |
| `tendril plan add-repo <plan-id> <path>` | Add repo to plan |
| `tendril plan remove-repo <plan-id> <path>` | Remove repo from plan |
| `tendril plan add-pr <plan-id> <url>` | Add PR to plan |
| `tendril plan add-commit <plan-id> <sha>` | Add commit to plan |
| `tendril plan add-related-plan <plan-id> <folder>` | Add related plan |
| `tendril plan remove-related-plan <plan-id> <folder>` | Remove related plan |
| `tendril plan add-depends-on <plan-id> <folder>` | Add dependency |
| `tendril plan remove-depends-on <plan-id> <folder>` | Remove dependency |
| `tendril plan add-log <plan-id> <action>` | Add execution log entry |
| `tendril plan write-revision <plan-id>` | Write revision from stdin |
| `tendril plan cleanup <plan-id>` | Remove worktrees |
| `tendril plan set-verification <plan-id> <name> <status>` | Set verification status |

### Plan Recommendation Commands

| Command | Description |
|---------|-------------|
| `tendril plan rec list <plan-id>` | List recommendations |
| `tendril plan rec add <plan-id> <title>` | Add recommendation |
| `tendril plan rec remove <plan-id> <title>` | Remove recommendation |
| `tendril plan rec set <plan-id> <title> <field> <value>` | Update recommendation field |
| `tendril plan rec accept <plan-id> <title>` | Accept recommendation |
| `tendril plan rec decline <plan-id> <title>` | Decline recommendation |

### Verification Definition Commands

| Command | Description |
|---------|-------------|
| `tendril verification list` | List verification definitions |
| `tendril verification get <name>` | Get verification details |
| `tendril verification add <name>` | Add verification definition |
| `tendril verification remove <name>` | Remove verification definition |
| `tendril verification set <name> <field> <value>` | Set verification field |

### Promptware Commands

| Command | Description |
|---------|-------------|
| `tendril promptware run <name>` | Run a promptware |
| `tendril promptware read-memory <name> <file>` | Read promptware memory |
| `tendril promptware write-memory <name> <file>` | Write promptware memory (stdin) |
| `tendril promptware write-tool <name> <file>` | Write promptware tool (stdin) |

### Project Commands

| Command | Description |
|---------|-------------|
| `tendril project list` | List projects |
| `tendril project get <name>` | Get project details |
| `tendril project add <name>` | Add project |
| `tendril project remove <name>` | Remove project |
| `tendril project set <name> <field> <value>` | Set project field |
| `tendril project add-repo <name> <path>` | Add repo to project |
| `tendril project remove-repo <name> <path>` | Remove repo from project |
| `tendril project add-verification <name> <ver>` | Add verification to project |
| `tendril project remove-verification <name> <ver>` | Remove verification from project |
| `tendril project add-review-action <name>` | Add review action |
| `tendril project remove-review-action <name> <action>` | Remove review action |

## Important Notes

- **Never read or write `plan.yaml` directly** -- always use `tendril plan` CLI commands.
- Verification statuses: `Pending`, `Pass`, `Fail`, `Skipped`.
- Plan states: `Draft`, `Building`, `Updating`, `Executing`, `ReadyForReview`, `Failed`, `Completed`, `Skipped`, `Blocked`, `Icebox`.
- To create a plan interactively: use `tendril plan create "<title>"` then `tendril plan write-revision <id> <<'EOF' ... EOF` to add content.
