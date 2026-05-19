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

#### job status

```terminal
>tendril job status <job-id> --message <text> [--plan-id <id>] [--plan-title <title>]
```

Writes a status update file for a running job. Used internally by agents to report progress visible in the Tendril UI.

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

