# Tendril Environment

You are running inside Tendril, a plan management and agentic orchestration system.

- **TENDRIL_HOME**: `D:/Tendril`
- **Plans folder**: `D:/Plans`
- **Config**: `D:/Tendril/config.yaml`
- **Database**: `D:/Tendril/tendril.db`

## Directory Structure

```
D:/Tendril/
  config.yaml          # Main configuration
  tendril.db           # SQLite database
  Plans/               # Plan folders ({ID}-{Title}/)
  Promptwares/         # Promptware programs
  Inbox/               # Incoming plan requests
  Trash/               # Discarded plans
  Hooks/               # Event hooks
  Logs/Jobs/           # Failed job output
```

## Tendril CLI Reference

The `tendril` CLI manages plans, projects, verifications, and system state.

Plan IDs accept: full path, folder name, zero-padded ID (e.g., `00015`), or bare number (e.g., `15`).

### Root Commands

| Command | Description |
|---------|-------------|
| `tendril doctor` | Check system health |
| `tendril db-version` | Show database version |
| `tendril db-migrate` | Run database migrations |
| `tendril db-reset` | Reset database |
| `tendril reset` | Reset Tendril state |
| `tendril update-promptwares` | Update promptware programs |
| `tendril version` | Show version |
| `tendril update` | Update Tendril CLI |
| `tendril report-bug` | Report a bug |
| `tendril models` | List available models and pricing |

### Job Commands

| Command | Description |
|---------|-------------|
| `tendril job status <job-id> --message "..."` | Report job status |

### Plan Commands

| Command | Description |
|---------|-------------|
| `tendril plan list` | List plans (supports filters) |
| `tendril plan create` | Create a new plan |
| `tendril plan update <plan-id>` | Update plan from stdin |
| `tendril plan set <plan-id> <field> <value>` | Set a plan field |
| `tendril plan get <plan-id> [field]` | Get plan data |
| `tendril plan validate <plan-id>` | Validate plan health |
| `tendril plan doctor` | Check all plans health |
| `tendril plan add-repo <plan-id> <path>` | Add repo to plan |
| `tendril plan remove-repo <plan-id> <path>` | Remove repo from plan |
| `tendril plan add-pr <plan-id> <url>` | Add PR to plan |
| `tendril plan add-commit <plan-id> <sha>` | Add commit to plan |
| `tendril plan add-related-plan <plan-id> <folder>` | Add related plan |
| `tendril plan remove-related-plan <plan-id> <folder>` | Remove related plan |
| `tendril plan add-depends-on <plan-id> <folder>` | Add dependency |
| `tendril plan remove-depends-on <plan-id> <folder>` | Remove dependency |
| `tendril plan add-log <plan-id> <action>` | Add execution log entry |
| `tendril plan write-revision <plan-id>` | Write revision from stdin |
| `tendril plan cleanup <plan-id>` | Remove worktrees |
| `tendril plan remove-worktree <plan-id>` | Remove a single worktree |
| `tendril plan sync-worktree <plan-id>` | Apply sync strategy |
| `tendril plan set-verification <plan-id> <name> <status>` | Set verification status |

### Plan Recommendation Commands

| Command | Description |
|---------|-------------|
| `tendril plan rec list <plan-id>` | List recommendations |
| `tendril plan rec add <plan-id> <title>` | Add recommendation |
| `tendril plan rec remove <plan-id> <title>` | Remove recommendation |
| `tendril plan rec set <plan-id> <title> <field> <value>` | Update recommendation field |
| `tendril plan rec accept <plan-id> <title>` | Accept recommendation |
| `tendril plan rec decline <plan-id> <title>` | Decline recommendation |

### Plan Verification Commands

| Command | Description |
|---------|-------------|
| `tendril plan verification list <plan-id>` | List plan verifications |
| `tendril plan verification add <plan-id> <name>` | Add verification to plan |
| `tendril plan verification remove <plan-id> <name>` | Remove verification from plan |

### Verification Definition Commands

| Command | Description |
|---------|-------------|
| `tendril verification list` | List verification definitions |
| `tendril verification get <name>` | Get verification details |
| `tendril verification add <name>` | Add verification definition |
| `tendril verification remove <name>` | Remove verification definition |
| `tendril verification set <name> <field> <value>` | Set verification field |

### Promptware Commands

| Command | Description |
|---------|-------------|
| `tendril promptware run <name>` | Run a promptware |
| `tendril promptware read-memory <name> <file>` | Read promptware memory |
| `tendril promptware write-memory <name> <file>` | Write promptware memory (stdin) |
| `tendril promptware write-tool <name> <file>` | Write promptware tool (stdin) |

### Trash Commands

| Command | Description |
|---------|-------------|
| `tendril trash write` | Write to trash from stdin |

### Project Commands

| Command | Description |
|---------|-------------|
| `tendril project list` | List projects |
| `tendril project get <name>` | Get project details |
| `tendril project add <name>` | Add project |
| `tendril project remove <name>` | Remove project |
| `tendril project set <name> <field> <value>` | Set project field |
| `tendril project add-repo <name> <path>` | Add repo to project |
| `tendril project remove-repo <name> <path>` | Remove repo from project |
| `tendril project add-verification <name> <ver>` | Add verification to project |
| `tendril project remove-verification <name> <ver>` | Remove verification from project |
| `tendril project move-verification <name> <ver> <pos>` | Move verification position |
| `tendril project add-build-dep <name> <dep>` | Add build dependency |
| `tendril project remove-build-dep <name> <dep>` | Remove build dependency |
| `tendril project add-review-action <name>` | Add review action |
| `tendril project remove-review-action <name> <action>` | Remove review action |

## Important Notes

- **Never read or write `plan.yaml` directly** — always use `tendril plan` CLI commands.
- Verification statuses: `Pending`, `Pass`, `Fail`, `Skipped`.
- Plan states: `Draft`, `Building`, `Updating`, `Executing`, `ReadyForReview`, `Failed`, `Completed`, `Skipped`, `Blocked`, `Icebox`.
