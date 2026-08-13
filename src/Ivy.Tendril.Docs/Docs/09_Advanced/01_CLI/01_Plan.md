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
Create, read, update, and validate plans from the terminal. All subcommands resolve the plan folder from `TENDRIL_PLANS`, `TENDRIL_HOME/Plans`, or `~/.tendril/Plans` when environment variables are unset.
</Ingress>

## CRUD

#### plan create

```terminal
>tendril plan create <title> <project> [options]
```

Creates a new plan folder and `plan.yaml` scaffold with state `Draft`. The plan ID is auto-allocated from the `.counter` file. Repos are derived from the project configuration.

| Option | Description |
|--------|-------------|
| `--level <level>` | Priority level (default: Feature) |
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
| `--project <name>` | Filter by project name (validated against configured projects) |
| `--level <level>` | Filter by level (e.g. `Bug`, `Feature`, `Epic`) |
| `--has-pr` | Only plans that have associated PRs |
| `--has-worktree` | Only plans that have worktrees |
| `--limit <n>` | Maximum number of results |
| `--format <fmt>` | Output format: `table` (default), `ids`, `folders`, `json` |
| `--plans-dir <path>` | Override plans directory path |

```terminal
>tendril plan list --state Draft
>tendril plan list --project Tendril --level Critical
>tendril plan list --state Failed --format ids
>tendril plan list --format json --limit 10
```

<Callout type="Info">
`plan list` shows plans (from `plan.yaml` files), not jobs. For job history and execution status, use `job list` instead (see [Other Commands](05_Other.md#job-list)).

</Callout>

#### plan get

```terminal
>tendril plan get <plan-id> [field]
```

Prints the full YAML, or a single field value when `[field]` is provided.

**Scalar fields:** `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`, `partialDelivery`

**List fields:** `repos`, `prs`, `commits`, `verifications`, `dependsOn`, `relatedPlans`, `recommendations` (each item on its own line)

#### plan set

```terminal
>tendril plan set <plan-id> <field> <value>
>tendril plan set <plan-id> state Completed --allow-failed-verifications
```

Updates a single field and bumps the `updated` timestamp automatically.

Setting `state` to `Completed` is refused while any verification is in the `Fail` state: a plan that reads as done while a gate rejected the work hides a missing deliverable from duplicate detection. Re-run the verification, or set it to `Skipped` with an explicit reason. `--allow-failed-verifications` records the transition anyway and sets `partialDelivery: true`, which marks the plan's deliverable as possibly missing.

#### plan update

```terminal
>cat revised.yaml | tendril plan update <plan-id> --stdin
```

Replaces the entire `plan.yaml` content from `--file` or `--stdin` (required — `--stdin` is not implicit).

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

#### plan add-worktree

```terminal
>tendril plan add-worktree <plan-id> <repo> [--base <branch>]
```

Creates a git worktree for the given plan under `<plan-folder>/Worktrees/<repo-name>`,
branching from origin/<base> (default: auto-detected default branch). The branch is
named `tendril/<plan-folder-name>` (e.g. `tendril/00025-AddSymmetricPlanAddWorktreeCLICommand`).

On failure (repo path missing, stale worktree, fetch failure, or git worktree add
failure), the command prints the specific step that failed along with git's raw
stderr and exits non-zero, instead of throwing a generic error - this lets an agent
read the exact git failure and decide how to recover.

#### plan remove-worktree

```terminal
>tendril plan remove-worktree <plan-id> <repo-name> [--branch <branch>]
```

Removes a single worktree from `Worktrees/<repo-name>`. Attempts `git worktree remove --force` first; falls back to a force-delete. Also deletes the associated branch (`tendril/<plan-folder>` by default).


## Revisions

Execution logs are written per job, not per plan — see `tendril job add-log`.

```terminal
>cat revision.md | tendril plan write-revision <plan-id> --stdin
>tendril plan write-revision <plan-id> --file revision.md
```

Writes a numbered revision file to `Revisions/` (e.g. `002.md`) from stdin or `--file`. Prints the path to stdout.

```terminal
>tendril plan get-revision <plan-id> [--latest] [--number <n>]
```

Prints revision content to stdout — the latest revision by default, or a specific numbered revision with `--number`.

## Questions

A revision can carry questions for the user in fenced `questions` blocks. Promptwares run headless and cannot ask anything mid-run, so a planning agent that hits an ambiguity it cannot research away emits a block instead. The user answers in the UI, which writes the answers back into the same block, and UpdatePlan then folds them into the plan and removes the questions.

````
```questions
questions:                    # 1-4 items
  - title:       string       # required, the question
    header:      string       # optional, <=12 char chip label
    description: markdown     # optional, context shown under the question
    multiple:    bool         # optional, default false; true = multi-select
    other:       bool         # optional, default true; user may type a free value
    options:                  # 2-4 items; omit entirely for a pure free-text question
      - title:       string   # required, 1-5 words
        description: markdown # optional
        value:       slug     # required, ^[a-z0-9][a-z0-9-]*$, referenced by `answer`
        recommended: bool     # optional, max one per question
    answer:      value | [values] | string   # filled in on response
```
````

A block may appear anywhere in the document, and a revision may contain any number of them. No `answer` key means unanswered; `answer: null` means the user deliberately skipped the question and left the decision to the agent.

`write-revision` validates every block and refuses the write if any is malformed, printing each problem prefixed with the line of its opening fence. Nothing is written on rejection, so a rejected revision does not consume a revision number. A block whose body predates the schema (plain prose rather than a `questions` mapping) is reported as a warning and written unchanged.

```terminal
>tendril plan write-revision <plan-id> --file revision.md --no-question-check
```

`--no-question-check` skips the validation. It exists for scripted and test use — promptwares should not use it.

## Recommendations

```terminal
>tendril plan rec list <plan-id> [--state <state>]
>tendril plan rec add <plan-id> <title> [-d <description>] [--impact <level>]
>tendril plan rec set <plan-id> <title> <field> <value>
>tendril plan rec accept <plan-id> <title> [--notes <text>]
>tendril plan rec decline <plan-id> <title> [--reason <text>]
>tendril plan rec remove <plan-id> <title>
```

Manage recommendations stored in a plan's YAML.

- **list** — filter by state: `Pending`, `Accepted`, `AcceptedWithNotes`, `Declined`
- **add** — impact levels: `Small`, `Medium`, `High`; provide `--description`, `--file`, or `--stdin`
- **set** — supported fields: `title`, `description`, `state`, `impact`, `declineReason`
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

### Partial delivery backfill

The report also lists plans sitting at `Completed` with a verification in the `Fail` state and no `partialDelivery` flag. These predate the completion guard, so duplicate detection reads them as fully delivered even though the deliverable may be missing.

Nothing is mutated: these are historical records, and whether a given plan really was a partial delivery is the user's call. Review each one, then flag the genuinely partial ones:

```terminal
>tendril plan set <id> state Completed --allow-failed-verifications
```
