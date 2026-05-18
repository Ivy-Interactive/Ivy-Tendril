---
icon: Terminal
searchHints:
  - cli
  - command
  - terminal
  - tendril
  - shell
  - doctor
  - health
  - diagnostics
  - plan
  - create
  - list
  - get
  - set
  - validate
  - repair
  - prune
  - repo
  - pr
  - commit
  - verification
  - database
  - db
  - migrate
  - reset
  - version
  - schema
  - mcp
  - hash
  - password
  - promptwares
  - run
  - server
---

# Command-Line Interface

<Ingress>
Tendril ships as a single `tendril` binary that doubles as both a web server and a CLI tool for managing plans, databases, diagnostics, and integrations.
</Ingress>

## Usage

```bash
tendril [command] [options]
```

When invoked without a recognized command, Tendril starts the desktop application.

## Global Options

These flags can be used with any Tendril CLI command to control output verbosity:

| Flag | Short | Effect |
|------|-------|--------|
| `--verbose` | `-v` | Enable detailed debug logging |
| `--quiet` | `-q` | Suppress informational messages (errors/warnings only) |

Examples:

```bash
# Show detailed debug output during plan creation
tendril plan list --verbose

# Run doctor with minimal output
tendril doctor --quiet

# Verbose promptware execution
tendril promptware run CreatePlan --verbose D:\Plans\00123-MyPlan
```

**Note:** Verbosity flags are inherited by child processes. When you run a promptware with `--verbose`, the spawned agent also runs in verbose mode.

## Commands at a Glance

| Command | Purpose |
|---------|---------|
| `run` | Start the Tendril server (with optional `--port`) |
| `version` | Print the installed version |
| `doctor` | System health check |
| `reset` | Remove all Tendril data and environment variables |
| `report-bug` | Submit a bug report with plan/job context |
| `plan <subcommand>` | Create and manage plans |
| `plan doctor` | Scan and repair plan folders |
| `verification <subcommand>` | Manage global verification definitions |
| `project <subcommand>` | Manage projects |
| `promptware <subcommand>` | Run promptwares and manage their memory/tools |
| `trash write` | Write a file to the Trash directory |
| `db-version` | Show database schema version |
| `db-migrate` | Apply pending migrations |
| `db-reset` | Reset the database |
| `mcp` | Launch the MCP server |
| `hash-password` | Generate an Argon2 password hash |
| `update-promptwares` | Refresh embedded promptware templates |

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `TENDRIL_HOME` | Root directory for config, database, inbox, and plans |
| `TENDRIL_PLANS` | Override plans directory (defaults to `TENDRIL_HOME/Plans`) |
| `TENDRIL_VERBOSE` | Enable verbose debug output (set to `1`) |
| `TENDRIL_QUIET` | Suppress non-essential output (set to `1`) |

## doctor

Run system diagnostics from the command line.

```bash
tendril doctor
```

Validates your Tendril installation:

| Check | Details |
|-------|---------|
| `TENDRIL_HOME` | Environment variable is set and directory exists |
| `config.yaml` | Configuration file is present and parseable |
| Required software | `gh` (authenticated), `git` |
| Optional software | `pandoc` |
| PowerShell | `pwsh` is available |
| Database | Schema is intact and current |
| Agent models | Configured coding agent is reachable |

<Callout type="Tip">
To check plan health specifically, use `tendril plan doctor` — see the plan section below.

</Callout>

## reset

Remove all Tendril data from the machine — use this to start fresh or fully uninstall.

```bash
tendril reset [--force]
```

Shows a summary of what will be deleted (directories and environment variables), asks for confirmation, then performs the deletion.

| Option | Effect |
|--------|--------|
| `--force` | Skip the interactive confirmation prompt |

What gets removed:

- The `TENDRIL_HOME` directory (config, database, inbox, plans if not overridden)
- The `TENDRIL_PLANS` directory if it differs from `TENDRIL_HOME`
- On **Windows**: the `TENDRIL_HOME` and `TENDRIL_PLANS` user-level environment variables are cleared automatically
- On **macOS/Linux**: a reminder is printed to manually remove the `export` lines from your shell rc file (`.bashrc`, `.zshrc`, etc.)

<Callout type="Warning">
This permanently deletes all Tendril data. Plan YAML files, logs, and the database are removed. There is no undo.

</Callout>

Example output:

```
The following items will be deleted:

Directory: /home/user/.tendril (exists, 142 files)
Env var: TENDRIL_HOME (check shell rc)

Proceed with reset? [y/n] y
✓ Deleted directory: /home/user/.tendril
Note: On Linux/Mac, please manually remove the export lines from your shell rc file.

Reset complete.
Please restart your terminal for environment variable changes to take effect.
```

## report-bug

Submit a bug report to the Tendril team with plan and job context attached.

```bash
tendril report-bug (--plan <plan-id> | --job <job-id>) [options]
```

Collects relevant files (plan YAML, logs, agent conversation transcripts) into a zip archive and uploads them to the Tendril bug report API, which creates a GitHub issue automatically. Either `--plan` or `--job` must be provided.

| Option | Effect |
|--------|--------|
| `--plan <plan-id>` | Include files from this plan folder |
| `--job <job-id>` | Include log files for this job ID |
| `--description` / `-d` | Bug description (prompted interactively if omitted) |
| `--yes` / `-y` | Skip the confirmation prompt |
| `--dry-run` | Show what would be sent without uploading |

<Callout type="Warning">
Collected files are attached to a **public** GitHub issue. If your plan contains sensitive data, use another reporting channel.

</Callout>

Example:

```bash
# Report a bug for a specific plan
tendril report-bug --plan 03430 --description "ExecutePlan crashes on worktree creation"

# Preview what would be sent without submitting
tendril report-bug --plan 03430 --dry-run

# Report by job ID, skip confirmation
tendril report-bug --job 00042 -y
```

## plan

Create, read, update, and validate plans directly from the terminal. All subcommands resolve the plan folder from `TENDRIL_PLANS` (or `TENDRIL_HOME/Plans`).

### plan create

```bash
tendril plan create <title> [options]
```

Creates a new plan folder and `plan.yaml` scaffold with state `Draft`. The plan ID is auto-allocated from the `.counter` file.

#### Options

| Option | Description |
|--------|-------------|
| `--project <name>` | Project name (default: Auto) |
| `--level <level>` | Priority level (default: NiceToHave) |
| `--initial-prompt <text>` | Initial prompt text |
| `--source-url <url>` | Source URL (GitHub issue or PR) |
| `--execution-profile <profile>` | Execution profile (deep or balanced) |
| `--priority <number>` | Priority number (default: 0) |
| `--repo <path>` | Repository path (repeatable) |
| `--verification <Name=Status>` | Verification entry (repeatable) |
| `--related-plan <folder>` | Related plan folder name (repeatable) |
| `--depends-on <folder>` | Dependency plan folder name (repeatable) |

#### Output

```
PlanId: 01234
Directory: /path/to/Plans/01234-SafeTitle
Plan created: 01234-SafeTitle
```

### plan list

```bash
tendril plan list [options]
```

Lists plans with optional filters. Scans all plan folders and displays matching results.

#### Options

| Option | Effect |
|--------|--------|
| `--state <state>` | Filter by state (e.g. `Draft`, `Executing`, `Failed`) |
| `--project <name>` | Filter by project name |
| `--level <level>` | Filter by level (e.g. `Bug`, `Critical`, `NiceToHave`, `Epic` — customizable in config) |
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

### plan get

```bash
tendril plan get <plan-id> [field]
```

Prints the full YAML, or a single field value when `[field]` is provided.

**Supported scalar fields:** `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`

**Supported list fields:** `repos`, `prs`, `commits`, `verifications`, `dependsOn`, `relatedPlans`, `recommendations` (each list item on its own line)

### plan set

```bash
tendril plan set <plan-id> <field> <value>
```

Updates a single field and bumps the `updated` timestamp automatically.

### plan update

```bash
cat revised.yaml | tendril plan update <plan-id>
```

Replaces the entire `plan.yaml` content from stdin.

### plan add-repo / remove-repo

```bash
tendril plan add-repo <plan-id> <repo-path>
tendril plan remove-repo <plan-id> <repo-path>
```

Manage the list of repositories associated with a plan. Idempotent — adding an existing repo is a no-op.

### plan add-pr

```bash
tendril plan add-pr <plan-id> <pr-url>
```

Append a pull request URL to the plan's PR list.

### plan add-commit

```bash
tendril plan add-commit <plan-id> <sha>
```

Append a commit SHA to the plan's commit list.

### plan add-related-plan

```bash
tendril plan add-related-plan <plan-id> <folder-name>
```

Add a related plan reference (for parent plans, split-from, follow-ups).

### plan add-depends-on

```bash
tendril plan add-depends-on <plan-id> <folder-name>
```

Add a blocking dependency on another plan. ExecutePlan will wait for the dependency to reach `Completed` state before executing this plan.

### plan remove-depends-on

```bash
tendril plan remove-depends-on <plan-id> <folder-name>
```

Remove a blocking dependency from the plan. The folder name must match exactly (case-insensitive).

### plan remove-related-plan

```bash
tendril plan remove-related-plan <plan-id> <folder-name>
```

Remove a related plan reference from the plan's `relatedPlans` list. The folder name must match exactly (case-insensitive).

### plan set-verification

```bash
tendril plan set-verification <plan-id> <name> <status>
```

Set verification status. Valid statuses: `Pending`, `Pass`, `Fail`, `Skipped`.

### plan validate

```bash
tendril plan validate <plan-id>
```

Checks that the plan has all required fields and is internally consistent. Exits with code `1` on failure.

### plan add-log

```bash
tendril plan add-log <plan-id> <action> [--summary <text>]
```

Appends a log entry to the plan's `Logs/` directory. Log files are numbered sequentially (`001-CreatePlan.md`, `002-ExecutePlan.md`, etc.) and printed to stdout so callers can capture the path.

| Option | Effect |
|--------|--------|
| `<action>` | Action name used as the filename suffix (e.g., `CreatePlan`, `ExecutePlan`) |
| `--summary` | Optional summary text appended to the log body |

Example:

```bash
tendril plan add-log 03430 ExecutePlan --summary "Completed in 4m 12s"
# prints: /path/to/Plans/03430-MyPlan/Logs/003-ExecutePlan.md
```

### plan write-revision

```bash
cat revision.md | tendril plan write-revision <plan-id>
tendril plan write-revision <plan-id> --file revision.md
```

Writes a revision file to the plan's `Revisions/` directory. Revisions are numbered sequentially (`001.md`, `002.md`, etc.). Content is read from stdin unless `--file` is provided. Prints the written file path to stdout.

| Option | Effect |
|--------|--------|
| `--file` / `-f` | Read content from a file instead of stdin |

### plan remove-worktree

```bash
tendril plan remove-worktree <plan-id> <repo-name> [--branch <branch>]
```

Removes a single git worktree from a plan's `Worktrees/` directory. Attempts a clean `git worktree remove --force` first; falls back to a force-delete if that fails. Also deletes the associated branch (`tendril/<plan-folder>` by default).

| Option | Effect |
|--------|--------|
| `<repo-name>` | Repository folder name inside the plan's `Worktrees/` directory |
| `--branch` | Branch name to delete (auto-derived from the plan folder name if omitted) |

### plan sync-worktree

```bash
tendril plan sync-worktree <worktree-path> [--strategy <strategy>] [--base-branch <branch>]
```

Applies a sync strategy to an existing worktree. Accepts an absolute path to the worktree directory.

| Option | Effect |
|--------|--------|
| `--strategy` | `fetch` (default, no-op), `rebase`, or `merge` |
| `--base-branch` | Base branch to sync with (required for `rebase` and `merge`) |

Example:

```bash
tendril plan sync-worktree /path/to/Plans/03430-MyPlan/Worktrees/MyRepo \
  --strategy rebase --base-branch main
```

### plan verification

Manage verifications directly on a plan's YAML.

#### plan verification list

```bash
tendril plan verification list <plan-id> [--status <status>]
```

Lists all verifications on the plan. Optionally filter by status (`Pending`, `Pass`, `Fail`, `Skipped`).

#### plan verification add

```bash
tendril plan verification add <plan-id> <name> [--status <status>]
```

Adds a verification entry to the plan. Default status is `Pending`.

#### plan verification remove

```bash
tendril plan verification remove <plan-id> <name>
```

Removes a verification entry from the plan by name (case-insensitive).

### plan rec (recommendations)

Manage recommendations stored in a plan's YAML.

#### plan rec list

```bash
tendril plan rec list <plan-id> [--state <state>]
```

Lists all recommendations. Optionally filter by state (`Pending`, `Accepted`, `AcceptedWithNotes`, `Declined`).

#### plan rec add

```bash
tendril plan rec add <plan-id> <title> [-d|--description <text>] [--impact <level>] [--risk <level>]
```

Adds a new recommendation. If `--description` is omitted, reads from stdin. Impact/risk levels: `Small`, `Medium`, `High`.

#### plan rec set

```bash
tendril plan rec set <plan-id> <title> <field> <value>
```

Updates a single field on an existing recommendation. Supported fields: `title`, `description`, `state`, `impact`, `risk`, `declineReason`.

#### plan rec accept

```bash
tendril plan rec accept <plan-id> <title> [--notes <text>]
```

Sets recommendation state to `Accepted` (or `AcceptedWithNotes` if `--notes` is provided).

#### plan rec decline

```bash
tendril plan rec decline <plan-id> <title> [--reason <text>]
```

Sets recommendation state to `Declined` with an optional reason.

#### plan rec remove

```bash
tendril plan rec remove <plan-id> <title>
```

Permanently removes a recommendation from the plan.

### plan cleanup

```bash
tendril plan cleanup <plan-id> [--force]
```

Removes git worktrees associated with a plan. By default only runs on plans in a terminal state (`Completed`, `Failed`, `Skipped`, `Icebox`). Use `--force` to skip that check.

### plan doctor

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

## Database

Manage the local SQLite database that stores plan sync data, recommendations, and cost tracking.

### db-version

```bash
tendril db-version
```

Prints the current schema version number.

### db-migrate

```bash
tendril db-migrate
```

Applies any pending migrations to bring the database schema up to date. Safe to run repeatedly — already-applied migrations are skipped.

### db-reset

```bash
tendril db-reset [--force]
```

Wipes all data and recreates the schema from scratch.

| Option | Effect |
|--------|--------|
| `--force` | Skip the interactive confirmation prompt |

<Callout type="Warning">
This permanently deletes all stored data (recommendations, sync state, cost history). Plan files on disk are not affected.

</Callout>

## Other Commands

Additional utilities for running the server, MCP integration, security, and maintenance.

### run

```bash
tendril run [--port <port>]
```

Starts the Tendril web server. Automatically applies pending database migrations before serving.

| Option | Effect |
|--------|--------|
| `--port` | Override the default listening port (5010) |

### version

```bash
tendril version
```

Prints the installed Tendril version (e.g. `1.0.18`).

### mcp

```bash
tendril mcp [args...]
```

Launches the Tendril MCP (Model Context Protocol) server for integration with AI coding agents like Claude Code. Additional arguments are forwarded to the MCP runtime. See [MCP Server](03_MCP.md) for available tools and configuration.

### hash-password

```bash
tendril hash-password <password> [secret]
```

Generates an Argon2id hash for use in Tendril's authentication system. If `[secret]` is omitted, a random one is generated and printed alongside the hash.

### update-promptwares

```bash
tendril update-promptwares
```

Refreshes the embedded promptware templates from the bundled source. Use after upgrading Tendril to pick up new or updated promptwares.

### promptware

```bash
tendril promptware run <promptware-name> [args...] [options]
```

Runs a promptware by name. Used primarily for testing or manual promptware execution.

| Option | Effect |
|--------|--------|
| `--profile <profile>` | Override agent profile (deep, balanced, quick) |
| `--working-dir <path>` | Working directory for the agent process |
| `--value <key=value>` | Additional firmware header values (repeatable) |

Example:

```bash
tendril promptware run CreatePlan "Fix the login bug" --value Project=Tendril
```

### promptware read-memory

```bash
tendril promptware read-memory <name> <filename>
```

Reads a memory file from a promptware's `Memory/` directory and prints its contents to stdout. Used by agents to load persisted learnings.

```bash
tendril promptware read-memory ExecutePlan cli-quirks.md
```

### promptware write-memory

```bash
cat content.md | tendril promptware write-memory <name> <filename>
```

Writes a memory file to a promptware's `Memory/` directory from stdin. Creates the directory if it does not exist. Prints the written file path to stdout.

```bash
echo "Always use --force when cleaning worktrees" | \
  tendril promptware write-memory ExecutePlan cli-quirks.md
```

### promptware write-tool

```bash
cat tool.md | tendril promptware write-tool <name> <filename>
```

Writes a tool definition file to a promptware's `Tools/` directory from stdin. Creates the directory if it does not exist. Prints the written file path to stdout.

```bash
cat my-tool.md | tendril promptware write-tool CreatePlan my-tool.md
```

## verification

Manage global verification definitions stored in `config.yaml`. These definitions can be referenced by projects and plans.

### verification list

```bash
tendril verification list
```

Lists all verification definitions with their name and a preview of the prompt.

### verification get

```bash
tendril verification get <name>
```

Prints the full prompt for a verification definition to stdout.

### verification add

```bash
tendril verification add <name> [--prompt <text>]
```

Adds a new verification definition. If `--prompt` is omitted, reads the prompt from stdin.

```bash
# Inline prompt
tendril verification add BuildPasses --prompt "Run dotnet build and confirm exit code 0"

# From file
cat build-check-prompt.md | tendril verification add BuildPasses
```

### verification remove

```bash
tendril verification remove <name>
```

Removes a verification definition by name (case-insensitive).

### verification set

```bash
tendril verification set <name> <field> <value>
```

Updates a single field on a verification definition. Supported fields: `name`, `prompt`.

```bash
tendril verification set BuildPasses prompt "Run dotnet build --no-restore and check for errors"
```

## project

Manage projects stored in `config.yaml`. Projects group repositories, verifications, build dependencies, and review actions together.

### project list

```bash
tendril project list
```

Lists all projects showing name, color, number of repos, and number of verifications.

### project get

```bash
tendril project get <name>
```

Shows full details for a project: repos, verifications, review actions, and build dependencies.

### project add

```bash
tendril project add <name> [--color <color>] [--context <text>]
```

Creates a new project.

| Option | Effect |
|--------|--------|
| `--color` | Display color for the project (e.g., `blue`, `#3b82f6`) |
| `--context` | Context/prompt text injected into agents working on this project |

### project remove

```bash
tendril project remove <name>
```

Removes a project by name (case-insensitive).

### project set

```bash
tendril project set <name> <field> <value>
```

Updates a single field on a project. Supported fields: `name`, `color`, `context`.

### project add-repo / remove-repo

```bash
tendril project add-repo <project-name> <repo-path> [options]
tendril project remove-repo <project-name> <repo-path>
```

Add or remove a repository from a project.

| Option | Effect |
|--------|--------|
| `--pr-rule` | PR rule for this repo (`default`, `yolo`) |
| `--base-branch` | Default base branch (e.g., `main`, `development`) |
| `--sync-strategy` | Worktree sync strategy (`fetch`, `pull`) |

### project add-verification / remove-verification

```bash
tendril project add-verification <project-name> <verification-name> [--required]
tendril project remove-verification <project-name> <verification-name>
```

Add or remove a verification from a project. Use `--required` to mark the verification as mandatory for plan completion.

### project add-build-dep / remove-build-dep

```bash
tendril project add-build-dep <project-name> <dependency>
tendril project remove-build-dep <project-name> <dependency>
```

Add or remove a build dependency from a project. Build dependencies are checked before an agent starts executing a plan.

### project add-review-action / remove-review-action

```bash
tendril project add-review-action <project-name> <name> [--command <cmd>] [--condition <expr>]
tendril project remove-review-action <project-name> <name>
```

Add or remove a review action from a project. Review actions are shell commands run automatically during plan review.

| Option | Effect |
|--------|--------|
| `--command` | Shell command to execute |
| `--condition` | Optional condition expression (e.g., `Test-Path "..."`) — action is skipped if condition is false |

Example:

```bash
tendril project add-review-action Tendril RunTests \
  --command "dotnet test --no-build" \
  --condition 'Test-Path "tests/"'
```

## job

### job status

```bash
tendril job status <job-id> --message <text> [--plan-id <id>] [--plan-title <title>]
```

Writes a status update file for a running job. Used internally by agents to report progress visible in the Tendril UI.

| Option | Effect |
|--------|--------|
| `--message` / `-m` | Status message to display |
| `--plan-id` | Plan ID associated with the job |
| `--plan-title` | Plan title associated with the job |

## trash

### trash write

```bash
cat content.md | tendril trash write <filename>
```

Writes a file to the `$TENDRIL_HOME/Trash/` directory from stdin. Used by agents to soft-delete content (e.g., duplicate plan files) instead of permanently removing it. Prints the written file path to stdout.

```bash
echo "# Duplicate plan" | tendril trash write DuplicateTitle.md
```
