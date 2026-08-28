---
searchHints:
  - promptware
  - memory
  - tool
  - job
  - status
  - mcp
  - hash
  - password
  - update
  - agent-instructions
  - instructions
  - prompt
---

# Other Commands

## promptware

#### promptware run

```terminal
>tendril promptware run <promptware-name> [args...] [options]
```

Runs a promptware by name.

| Option | Effect |
|--------|--------|
| `--profile <profile>` | Override agent profile (`deep`, `balanced`, `quick`) |
| `--working-dir <path>` | Working directory for the agent process |
| `--value <key=value>` | Additional firmware header values (repeatable) |

```terminal
>tendril promptware run CreatePlan "Fix the login bug" --value Project=Tendril
```

#### promptware read-memory / write-memory / write-tool

```terminal
>tendril promptware read-memory <name> <filename>
>cat content.md | tendril promptware write-memory <name> <filename> --stdin
>cat tool.md | tendril promptware write-tool <name> <filename> --stdin
```

Read and write files in a promptware's `Memory/` and `Tools/` directories. Used by agents to persist and reload learned patterns and custom tool definitions. Write commands print the file path to stdout.

```terminal
>tendril promptware read-memory ExecutePlan cli-quirks.md
>echo "Always use --force when cleaning worktrees" | \
>  tendril promptware write-memory ExecutePlan cli-quirks.md --stdin
```

## job

#### job list

```terminal
>tendril job list
>tendril job list --project Ivy-Tendril --status Running
>tendril job list --type ExecutePlan --limit 20
>tendril job list --format json
```

Lists jobs from the Tendril database. Works without a running server by directly reading `tendril.db`. Filters by project, status, type, or plan ID. Results are ordered with in-flight jobs (NULL `CompletedAt`) first, then by most recent start time.

| Option | Effect |
|--------|--------|
| `--project <name>` | Filter by project name (validated against configured projects) |
| `--status <status>` | Filter by status (`Pending`, `Queued`, `Running`, `Completed`, `Failed`, `Timeout`, `Stopped`, `Blocked`) |
| `--type <type>` | Filter by job type (e.g., `CreatePlan`, `ExecutePlan`, `CreatePr`) |
| `--plan <id>` | Filter by plan ID |
| `--limit <n>` | Maximum results (default: 50) |
| `--format <fmt>` | Output format: `table` (default), `ids`, `json` |

```terminal
>tendril job list --project Ivy-Tendril --status Failed
>tendril job list --plan 00152 --format ids
```

<Callout type="Tip">
Unlike `plan list`, `job list` reads directly from the database and does not require the Tendril server to be running. It only reads `tendril.db` from `TENDRIL_HOME`, falling back to `~/.tendril` when the environment variable is unset.

</Callout>

#### job start

```terminal
>tendril job start <job-type> <plan-id> [options]
```

Starts a job on the running Tendril server. Requires Tendril to be running (communicates via HTTP).

| Job Type | Required Options | Optional |
|----------|-----------------|----------|
| `ExecutePlan` | `<plan-id>` | `--note` |
| `UpdatePlan` | `<plan-id>`, `--instructions` | — |
| `SplitPlan` | `<plan-id>` | — |
| `ExpandPlan` | `<plan-id>` | — |
| `CreateIssue` | `<plan-id>`, `--repo` | `--assignee`, `--comment`, `--labels` |
| `CreatePr` | `<plan-id>` | `--no-merge`, `--no-delete-branch`, `--no-artifacts`, `--assignee`, `--reviewer`, `--comment`, `--draft` |
| `RetryPlan` | `<plan-id>`, `--change-request` | — |
| `CreatePlan` | `--description`, `--project` | `--priority`, `--force`, `--source-path` |

```terminal
>tendril job start ExecutePlan 00042
>tendril job start RetryPlan 00042 --change-request "Fix the failing tests"
>tendril job start CreatePlan --description "Add dark mode" --project MyProject
```

<Callout type="Info">
The Tendril server must be running for this command to work. It discovers the server via the `.master` lock file in `TENDRIL_HOME`.

</Callout>

#### job status

```terminal
>tendril job status <job-id> --message <text> [--plan-id <id>] [--plan-title <title>]
```

Reports a status update to the running Tendril server for a job in progress. Used internally by agents to report progress visible in the Tendril UI.

| Option | Effect |
|--------|--------|
| `--message` / `-m` | Status message to display |
| `--plan-id` | Plan ID associated with the job |
| `--plan-title` | Plan title associated with the job |

#### job add-log

```terminal
>tendril job add-log <job-id> <action> [--summary <text>]
```

Appends an `## Agent Log` section to the job's log in `<TendrilHome>/Jobs/` and prints the path to
stdout. Writes straight to disk, so unlike `job start` and `job status` it does not need the Tendril
server to be running. Agents pass the `TendrilJobId` firmware header value as `<job-id>`.

| Option | Effect |
|--------|--------|
| `--summary` | Body text for the log entry |

## agent-instructions

```terminal
>tendril agent-instructions
```

Prints the compiled agent system prompt — the same instructions the Agent app gives the interactive assistant — to stdout, with `{TENDRIL_HOME}` and `{PLAN_FOLDER}` substituted from `config.yaml`. Exits 1 if the embedded prompt resource is missing. Useful for piping into another agent or diffing prompt changes.

