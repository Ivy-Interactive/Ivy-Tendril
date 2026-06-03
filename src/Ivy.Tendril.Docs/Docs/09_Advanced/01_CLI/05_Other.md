---
searchHints:
  - promptware
  - memory
  - tool
  - job
  - status
  - trash
  - mcp
  - hash
  - password
  - update
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
>cat content.md | tendril promptware write-memory <name> <filename>
>cat tool.md | tendril promptware write-tool <name> <filename>
```

Read and write files in a promptware's `Memory/` and `Tools/` directories. Used by agents to persist and reload learned patterns and custom tool definitions. Write commands print the file path to stdout.

```terminal
>tendril promptware read-memory ExecutePlan cli-quirks.md
>echo "Always use --force when cleaning worktrees" | \
>  tendril promptware write-memory ExecutePlan cli-quirks.md
```

## job

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
| `CreatePr` | `<plan-id>` | `--no-merge`, `--no-delete-branch`, `--no-artifacts`, `--assignee`, `--comment`, `--draft` |
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

## trash

#### trash write

```terminal
>cat content.md | tendril trash write <filename>
```

Writes a file to `$TENDRIL_HOME/Trash/` from stdin. Used by agents to soft-delete content (e.g. duplicate plan files) instead of permanently removing it. Prints the written file path to stdout.

