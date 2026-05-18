---
searchHints:
  - cli
  - command
  - terminal
  - tendril
  - shell
  - reset
  - report-bug
  - run
  - doctor
  - version
---

# CLI Overview

<Ingress>
Manage plans, projects, databases, and agents directly from your terminal. The `tendril` binary works as both a web server and a full-featured CLI tool.
</Ingress>

Tendril CLI gives you complete control over your workflow without touching the UI:

- **Plans** — create, list, update, and inspect plans; manage repos, worktrees, verifications, and recommendations
- **Projects** — configure projects, their repos, build dependencies, and review actions
- **Verifications** — define and manage reusable verification checks
- **Database** — run migrations, inspect schema versions, or reset the database
- **Agents** — run and manage promptwares and their memory

## Quick Start

**1. Check your installation**

```bash
tendril doctor
```

**2. Start the web server**

```bash
tendril run
```

**3. Create a new plan**

```bash
tendril plan create "Fix login bug" --project MyProject
```

**4. List active plans**

```bash
tendril plan list --state Executing
```

**5. Reset everything and start fresh**

```bash
tendril reset
```

<Callout type="Tip">
Every command supports `--help` for detailed usage. For example: `tendril plan create --help`.

</Callout>

## Global Options

| Flag | Short | Effect |
|------|-------|--------|
| `--verbose` | `-v` | Enable detailed debug logging |
| `--quiet` | `-q` | Suppress informational messages (errors/warnings only) |

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `TENDRIL_HOME` | Root directory for config, database, inbox, and plans |
| `TENDRIL_PLANS` | Override plans directory (defaults to `TENDRIL_HOME/Plans`) |
| `TENDRIL_VERBOSE` | Enable verbose debug output (set to `1`) |
| `TENDRIL_QUIET` | Suppress non-essential output (set to `1`) |

## Common Commands

#### doctor

```bash
tendril doctor
```

Validates your Tendril installation — checks `TENDRIL_HOME`, `config.yaml`, required tools (`gh`, `git`), optional tools (`pandoc`, `pwsh`), database schema, and agent model availability. Always a good first step when something isn't working.

#### run

```bash
tendril run
tendril run --port 8080
```

Starts the Tendril web server. Automatically applies pending database migrations before serving. Default port is `5010`.

#### reset

```bash
tendril reset
tendril reset --force
```

Removes all Tendril data from the machine — deletes `TENDRIL_HOME`, `TENDRIL_PLANS`, and clears environment variables. On macOS/Linux, prints a reminder to remove the `export` lines from your shell rc file manually.

<Callout type="Warning">
This permanently deletes all data. There is no undo.

</Callout>

#### report-bug

```bash
tendril report-bug --plan 03430
tendril report-bug --job 00042 --description "Agent crashes on worktree creation"
tendril report-bug --plan 03430 --dry-run
```

Collects plan files and agent logs into a zip archive and submits them to the Tendril bug report API, which opens a GitHub issue automatically.

| Option | Effect |
|--------|--------|
| `--plan <plan-id>` | Include files from this plan folder |
| `--job <job-id>` | Include log files for this job ID |
| `--description` / `-d` | Bug description (prompted interactively if omitted) |
| `--yes` / `-y` | Skip the confirmation prompt |
| `--dry-run` | Show what would be sent without uploading |

<Callout type="Warning">
Attached files are posted to a **public** GitHub issue. If your plan contains sensitive data, use another reporting channel.

</Callout>

#### version

```bash
tendril version
```

Prints the installed Tendril version (e.g. `1.0.34`).

#### update-promptwares

```bash
tendril update-promptwares
```

Refreshes the embedded promptware templates from the bundled source. Run after upgrading Tendril to pick up new or updated promptwares.

## Next Steps

- [Plan commands](01_Plan.md) — full reference for creating and managing plans
- [Project commands](02_Project.md) — configure projects, repos, and review actions
- [Verification commands](03_Verification.md) — manage global verification definitions
- [Database commands](04_Database.md) — migrations, schema version, and reset
- [Other commands](05_Other.md) — promptware, job, trash, MCP, and utilities
