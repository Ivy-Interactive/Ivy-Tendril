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

## Commands at a Glance

| Command | Purpose |
|---------|---------|
| `run` | Start the Tendril server (with optional `--port`) |
| `version` | Print the installed version |
| `doctor` | System health check |
| `plan <subcommand>` | Create and manage plans |
| `plan doctor` | Scan and repair plan folders |
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

## plan

Create, read, update, and validate plans directly from the terminal. All subcommands resolve the plan folder from `TENDRIL_PLANS` (or `TENDRIL_HOME/Plans`).

### plan create

```bash
tendril plan create <plan-id> <title>
```

Creates a new plan folder and `plan.yaml` scaffold with state `Draft`.

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

### plan get

```bash
tendril plan get <plan-id> [field]
```

Prints the full YAML, or a single field value when `[field]` is provided.

**Supported fields:** `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`

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
