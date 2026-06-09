---
searchHints:
  - plan
  - create
  - list
  - get
  - set
  - update
  - validate
  - repo
  - pr
  - commit
  - worktree
  - verification
  - recommendation
  - rec
  - log
  - revision
  - doctor
  - depends
  - related
---

# plan

<Ingress>
Create, read, update, and validate plans from the terminal. All subcommands resolve the plan folder from `TENDRIL_PLANS` (or `TENDRIL_HOME/Plans`).
</Ingress>

## CRUD

#### plan create

```terminal
>tendril plan create <title> <project> [options]
```

Creates a new plan folder and `plan.yaml` scaffold with state `Draft`. The plan ID is auto-allocated from the `.counter` file. Repos are derived from the project configuration.

| Option | Description |
|--------|-------------|
| `--level <level>` | Priority level (default: NiceToHave) |
| `--initial-prompt <text>` | Initial prompt text |
| `--source-url <url>` | Source URL (GitHub issue or PR) |
| `--execution-profile <profile>` | Execution profile (`deep` or `balanced`) |
| `--priority <number>` | Priority number (default: 0) |
| `--verification <Name=Status>` | Verification entry (repeatable) |
| `--related-plan <folder>` | Related plan folder name (repeatable) |
| `--depends-on <folder>` | Dependency plan folder name (repeatable) |

#### plan list

```terminal
>tendril plan list [options]
```

Lists plans with optional filters.

| Option | Effect |
|--------|--------|
| `--state <state>` | Filter by state (e.g. `Draft`, `Executing`, `Failed`) |
| `--project <name>` | Filter by project name |
| `--level <level>` | Filter by level (e.g. `Bug`, `Critical`, `NiceToHave`) |
| `--has-pr` | Only plans that have associated PRs |
| `--has-worktree` | Only plans that have worktrees |
| `--limit <n>` | Maximum number of results |
| `--format <fmt>` | Output format: `table` (default), `ids`, `folders`, `json` |

```terminal
>tendril plan list --state Draft
>tendril plan list --project Tendril --level Critical
>tendril plan list --state Failed --format ids
>tendril plan list --format json --limit 10
```

#### plan get

```terminal
>tendril plan get <plan-id> [field]
```

Prints the full YAML, or a single field value when `[field]` is provided.

**Scalar fields:** `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`

**List fields:** `repos`, `prs`, `commits`, `verifications`, `dependsOn`, `relatedPlans`, `recommendations` (each item on its own line)

#### plan set

```terminal
>tendril plan set <plan-id> <field> <value>
```

Updates a single field and bumps the `updated` timestamp automatically.

#### plan update

```terminal
>cat revised.yaml | tendril plan update <plan-id>
```

Replaces the entire `plan.yaml` content from stdin.

#### plan validate

```terminal
>tendril plan validate <plan-id>
```

Checks that the plan has all required fields and is internally consistent. Exits with code `1` on failure.

## Repos

```terminal
>tendril plan add-repo <plan-id> <repo-path>
>tendril plan remove-repo <plan-id> <repo-path>
```

Manage the list of repositories associated with a plan. Adding an existing repo is a no-op.

## Links

```terminal
>tendril plan add-pr <plan-id> <pr-url>
>tendril plan add-commit <plan-id> <sha>
>tendril plan add-related-plan <plan-id> <folder-name>
>tendril plan remove-related-plan <plan-id> <folder-name>
>tendril plan add-depends-on <plan-id> <folder-name>
>tendril plan remove-depends-on <plan-id> <folder-name>
```

Manage PR URLs, commit SHAs, related plans, and blocking dependencies. `add-depends-on` makes ExecutePlan wait for the dependency to reach `Completed` state before executing. All names are matched case-insensitively.

## Verifications

```terminal
>tendril plan set-verification <plan-id> <name> <status>
>tendril plan verification list <plan-id> [--status <status>]
>tendril plan verification add <plan-id> <name> [--status <status>]
>tendril plan verification remove <plan-id> <name>
```

Manage verifications on a plan. Valid statuses: `Pending`, `Pass`, `Fail`, `Skipped`. Default status for `add` is `Pending`.

## Worktrees

#### plan cleanup

```terminal
>tendril plan cleanup <plan-id> [--force]
```

Removes all git worktrees associated with a plan. By default only runs on plans in a terminal state (`Completed`, `Failed`, `Skipped`, `Icebox`). Use `--force` to skip that check.

#### plan remove-worktree

```terminal
>tendril plan remove-worktree <plan-id> <repo-name> [--branch <branch>]
```

Removes a single worktree from `Worktrees/<repo-name>`. Attempts `git worktree remove --force` first; falls back to a force-delete. Also deletes the associated branch (`tendril/<plan-folder>` by default).


## Logs & Revisions

```terminal
>tendril plan add-log <plan-id> <action> [--summary <text>]
```

Appends a numbered log entry to `Logs/` (e.g. `003-ExecutePlan.md`) and prints the path to stdout.

```terminal
>cat revision.md | tendril plan write-revision <plan-id>
>tendril plan write-revision <plan-id> --file revision.md
```

Writes a numbered revision file to `Revisions/` (e.g. `002.md`) from stdin or `--file`. Prints the path to stdout.

## Recommendations

```terminal
>tendril plan rec list <plan-id> [--state <state>]
>tendril plan rec add <plan-id> <title> [-d <description>] [--impact <level>] [--risk <level>]
>tendril plan rec set <plan-id> <title> <field> <value>
>tendril plan rec accept <plan-id> <title> [--notes <text>]
>tendril plan rec decline <plan-id> <title> [--reason <text>]
>tendril plan rec remove <plan-id> <title>
```

Manage recommendations stored in a plan's YAML.

- **list** — filter by state: `Pending`, `Accepted`, `AcceptedWithNotes`, `Declined`
- **add** — impact/risk levels: `Small`, `Medium`, `High`; reads description from stdin if `-d` is omitted
- **set** — supported fields: `title`, `description`, `state`, `impact`, `risk`, `declineReason`
- **accept** — sets state to `Accepted`, or `AcceptedWithNotes` if `--notes` is provided
- **decline** — sets state to `Declined` with an optional reason

## Doctor

```terminal
>tendril plan doctor [options]
```

Scans every folder in the plans directory and reports health issues.

| Option | Effect |
|--------|--------|
| `--all` | Show all plans (default hides healthy ones) |
| `--fix` | Automatically repair detected issues |
| `--prune` | Remove empty/junk plan folders |
| `--state <state>` | Filter by plan state |
| `--worktrees` | Show only plans with worktrees |

| Health Code | Meaning |
|-------------|---------|
| `YAML:Missing` | No `plan.yaml` in the folder |
| `YAML:Empty` | File exists but is empty |
| `YAML:No repos` | Plan has no repositories configured |
| `YAML:Missing title` | Title field is blank |
| `YAML:Missing project` | Project field is blank |
| `StaleWorktree` | Worktree directory exists without a valid `.git` pointer |
| `NestedWorktree` | Worktree contains nested git checkouts |

With `--fix`: creates scaffold YAML for missing files, fills in missing fields, and removes stale or nested worktrees.

```terminal
>tendril plan doctor
>tendril plan doctor --fix
>tendril plan doctor --prune
```
