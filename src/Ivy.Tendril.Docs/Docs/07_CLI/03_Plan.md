---
icon: FileText
searchHints:
  - plan
  - create
  - list
  - get
  - set
  - validate
  - doctor
  - repair
  - prune
  - repo
  - pr
  - commit
  - verification
---

<Text Color="Green" Small Bold>CLI</Text>

# plan

<Ingress>
Create, read, update, and validate plans directly from the terminal. All subcommands resolve the plan folder from `TENDRIL_PLANS` (or `TENDRIL_HOME/Plans`).
</Ingress>

## Subcommands

### create

```bash
tendril plan create <plan-id> <title>
```

Creates a new plan folder and `plan.yaml` scaffold with state `Draft`.

### list

```bash
tendril plan list [options]
```

Lists plans with optional filters. Scans all plan folders and displays matching results.

#### Options

| Option | Effect |
|--------|--------|
| `--state <state>` | Filter by state (e.g. `Draft`, `Executing`, `Failed`) |
| `--project <name>` | Filter by project name |
| `--level <level>` | Filter by level (`Critical`, `Bug`, `NiceToHave`, `Backlog`, `Icebox`) |
| `--has-pr` | Only plans that have associated PRs |
| `--has-worktree` | Only plans that have worktrees |
| `--limit <n>` | Maximum number of results |
| `--format <fmt>` | Output format: `table` (default), `ids`, `folders`, `json` |

#### Examples

```bash
# All draft plans
tendril plan list --state Draft

# Critical plans in the Tendril project
tendril plan list --project Tendril --level Critical

# Get plan IDs only (useful for scripting)
tendril plan list --state Failed --format ids

# JSON output for programmatic consumption
tendril plan list --format json --limit 10
```

### get

```bash
tendril plan get <plan-id> [field]
```

Prints the full YAML, or a single field value when `[field]` is provided.

**Supported fields:** `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`

### set

```bash
tendril plan set <plan-id> <field> <value>
```

Updates a single field and bumps the `updated` timestamp automatically.

### update

```bash
cat revised.yaml | tendril plan update <plan-id>
```

Replaces the entire `plan.yaml` content from stdin.

### add-repo / remove-repo

```bash
tendril plan add-repo <plan-id> <repo-path>
tendril plan remove-repo <plan-id> <repo-path>
```

Manage the list of repositories associated with a plan. Idempotent — adding an existing repo is a no-op.

### add-pr

```bash
tendril plan add-pr <plan-id> <pr-url>
```

Append a pull request URL to the plan's PR list.

### add-commit

```bash
tendril plan add-commit <plan-id> <sha>
```

Append a commit SHA to the plan's commit list.

### set-verification

```bash
tendril plan set-verification <plan-id> <name> <status>
```

Set verification status. Valid statuses: `Pending`, `Pass`, `Fail`, `Skipped`.

### validate

```bash
tendril plan validate <plan-id>
```

Checks that the plan has all required fields and is internally consistent. Exits with code `1` on failure.

### rec (recommendations)

Manage recommendations stored in a plan's YAML.

#### rec list

```bash
tendril plan rec list <plan-id> [--state <state>]
```

Lists all recommendations. Optionally filter by state (`Pending`, `Accepted`, `AcceptedWithNotes`, `Declined`).

#### rec add

```bash
tendril plan rec add <plan-id> <title> [-d|--description <text>] [--impact <level>] [--risk <level>]
```

Adds a new recommendation. If `--description` is omitted, reads from stdin. Impact/risk levels: `Small`, `Medium`, `High`.

#### rec set

```bash
tendril plan rec set <plan-id> <title> <field> <value>
```

Updates a single field on an existing recommendation. Supported fields: `title`, `description`, `state`, `impact`, `risk`, `declineReason`.

#### rec accept

```bash
tendril plan rec accept <plan-id> <title> [--notes <text>]
```

Sets recommendation state to `Accepted` (or `AcceptedWithNotes` if `--notes` is provided).

#### rec decline

```bash
tendril plan rec decline <plan-id> <title> [--reason <text>]
```

Sets recommendation state to `Declined` with an optional reason.

#### rec remove

```bash
tendril plan rec remove <plan-id> <title>
```

Permanently removes a recommendation from the plan.

### cleanup

```bash
tendril plan cleanup <plan-id> [--force]
```

Removes git worktrees associated with a plan. By default only runs on plans in a terminal state (`Completed`, `Failed`, `Skipped`, `Icebox`). Use `--force` to skip that check.

### doctor

```bash
tendril plan doctor [options]
```

Scans every folder in the plans directory and reports health issues.

#### Options

| Option | Effect |
|--------|--------|
| `--all` | Show all plans (default hides healthy ones) |
| `--fix` | Automatically repair detected issues |
| `--prune` | Remove empty/junk plan folders |
| `--state <state>` | Filter by plan state (e.g. `Draft`, `Failed`) |
| `--worktrees` | Show only plans with worktrees |

#### Detected Issues

| Health Code | Meaning |
|-------------|---------|
| `YAML:Missing` | No `plan.yaml` in the folder |
| `YAML:Empty` | File exists but is empty |
| `YAML:No repos` | Plan has no repositories configured |
| `YAML:Missing title` | Title field is blank |
| `YAML:Missing project` | Project field is blank |
| `StaleWorktree` | Worktree directory exists without a valid `.git` pointer |
| `NestedWorktree` | Worktree contains nested git checkouts |

#### Repair Behavior (`--fix`)

- **Missing YAML** — Creates a scaffold `plan.yaml` with state `Draft` and title derived from the folder name.
- **Missing fields** — Fills `title`, `project`, or `repos` with sensible defaults.
- **Stale worktrees** — Deletes orphaned worktree directories.
- **Nested worktrees** — Removes nested plan/worktree artifacts inside a worktree.

#### Example

```bash
# Show unhealthy plans only
tendril plan doctor

# Repair everything automatically
tendril plan doctor --fix

# Remove empty test/junk plans
tendril plan doctor --prune
```
