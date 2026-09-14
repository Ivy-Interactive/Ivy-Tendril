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
  - master
  - claim
  - release
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

## master

Inspects and, when necessary, breaks the master claim in `<TendrilHome>/.master` — the file every CLI command uses to find the running server. Reach for these when commands start reporting that the server is not running, or is hung, while you can see it in front of you.

#### master status

```terminal
>tendril master status
>tendril master status --json
```

Prints the claim (PID, port, scheme, start time, heartbeat age) and probes `/ivy/health` on the recorded address to say whether the process it names is actually serving. Never writes to or deletes `.master`, so looking does not destroy the evidence.

| Option | Effect |
|--------|--------|
| `--json` | Emit the report as JSON instead of a table |

Exits 0 when the holder is alive and answering — including when its heartbeat is late — and 1 otherwise, so a script can gate on it without parsing anything.

| Verdict | Meaning |
|---------|---------|
| `Healthy` | Alive, beating and answering on the scheme it recorded |
| `SaturatedButServing` | Answering, heartbeat late. The server is busy, not gone: leave the claim alone |
| `WedgedNotServing` | The process is running but answers on neither scheme, or has not published a port yet |
| `SchemeMismatch` | Only the other scheme answers, so the recorded one is what every command fails against |
| `DeadHolder` | The recorded PID is not running. The next launch reclaims the claim automatically |
| `NoClaim` | No `.master` at all, so nothing can discover the server |
| `Unreadable` | A claim exists but does not parse, which is what an interrupted write leaves behind |

#### master release

```terminal
>tendril master release
>tendril master release --yes
>tendril master release --force
```

Deletes the claim. Prompts for confirmation first, and refuses outright when the process it names is alive and answering `/ivy/health`.

| Option | Effect |
|--------|--------|
| `--yes` / `-y` | Skip the confirmation prompt |
| `--force` | Release even a master that is alive and answering (implies `--yes`) |

<Callout type="Warning">
Releasing the claim of a server that is still running lets the next launch come up as a second master against the same `TENDRIL_HOME`, with both running jobs. Stop the process first, or trust `master status`: a late heartbeat on its own is not a reason to release anything.

</Callout>

#### Recovering a missing or wedged claim

1. Run `tendril master status`.
2. `SaturatedButServing` — do nothing. The server is running and answering; its heartbeat will catch up when load drops.
3. `NoClaim` — a running master re-asserts its own claim within 30 seconds, logging `Re-asserted the master claim` to `<TendrilHome>/crash.log`. Wait one beat and look again; if nothing appears, no server is running, so start one with `tendril`.
4. `Unreadable` — same as above: a running master replaces it on its next beat. Otherwise `tendril master release --yes` and start a server.
5. `DeadHolder` — nothing to do; the next launch reclaims it. `tendril master release --yes` removes it now.
6. `WedgedNotServing` or `SchemeMismatch` — stop the process the claim names, then `tendril master release --yes` and start a server.

<Callout type="Info">
Every claim transition — taken, published, re-asserted, released, refused — is appended to `<TendrilHome>/crash.log`, which is written directly to disk and so survives a server too wedged to log anything else. That file is the history behind whatever `master status` shows you now.

</Callout>

## agent-instructions

```terminal
>tendril agent-instructions
```

Prints the compiled agent system prompt — the same instructions the Agent app gives the interactive assistant — to stdout, with `{TENDRIL_HOME}` and `{PLAN_FOLDER}` substituted from `config.yaml`. Exits 1 if the embedded prompt resource is missing. Useful for piping into another agent or diffing prompt changes.

