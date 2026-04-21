# Ivy Tendril — Agent Instructions

## Plan Schema Migrations

When changing the `plan.yaml` structure (adding/removing/renaming fields, changing field types):

1. **Update `Plans.md`** (`Promptwares/.shared/Plans.md`) — this is the source of truth for the plan schema
2. **Add a repair step** in `PlanReaderService.RepairPlans()` — this runs on every Tendril startup and must migrate all existing plans to the new format
3. **Keep `PlanYaml.cs` in sync** — the deserialization model must match what `Plans.md` documents
4. **Update promptware instructions** — any promptware that writes `plan.yaml` (CreatePlan, ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan) must produce the new format

Existing plans on disk are never recreated — they must be repaired in place. If `RepairPlans()` can't fix a plan, it will silently fail and that plan won't appear in the UI. Always test your repair logic against real plan files.

## Project Structure

- `Services/` — ConfigService, PlanReaderService, JobService, GitService
- `Apps/` — PlansApp, ReviewApp, JobsApp, IceboxApp, and their views
- `Promptwares/` — CreatePlan, ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan, CreatePr, IvyFrameworkVerification
- `Promptwares/.shared/` — Shared utilities (Utils.ps1, Plans.md, Firmware.md)
- `AppShell/` — Custom TendrilAppShell with sidebar badges

## Config

### Environment Variables

Tendril uses these environment variables:

- **`TENDRIL_HOME`** (required): Base path for all Tendril data (Plans/, Inbox/, Trash/, config.yaml, etc.)
  - Must be set before starting Tendril, otherwise onboarding is triggered
  - Example: `/home/user/.tendril` or `C:\Users\User\.tendril`

### Path Resolution

All paths derive from these sources:
1. `TENDRIL_HOME` environment variable (required) - points to config directory
2. Standard environment variables expanded via `%VAR%` syntax in config.yaml
3. Firmware header variables (`PlanFolder`, `PlansDirectory`, `ArtifactsDir`, etc.) derived from TENDRIL_HOME

**Never hardcode absolute paths** like `D:\Tendril` or `D:\Plans` in code or promptware instructions — always use the config values or firmware header variables.

## MCP Server

Tendril includes a built-in MCP (Model Context Protocol) server that exposes plan data and operations to agents. Start it with:

```bash
tendril mcp
```

The server runs over stdio and exposes these tools:

- **`tendril_get_plan`** — Fetch plan metadata and latest revision by ID (e.g., `03228`) or folder path
- **`tendril_list_plans`** — Query plans by state, project, or date range (returns up to 50 results)
- **`tendril_inbox`** — Create a new plan by writing to the Tendril inbox (picked up by InboxWatcherService)
- **`tendril_transition_plan`** — Change a plan's state (e.g., Draft → Executing)

### Authentication

The MCP server supports **optional bearer token authentication** for multi-user or remote access scenarios:

- **Without authentication** (default): Set no environment variable — all requests are allowed
- **With authentication**: Set `TENDRIL_MCP_TOKEN` environment variable — all tool calls require validation

**Enabling authentication:**

1. Generate a secure token (e.g., `openssl rand -base64 32`)
2. Set `TENDRIL_MCP_TOKEN` in your environment (shell profile, systemd service, etc.)
3. Configure Claude Code to pass the same token by setting `TENDRIL_MCP_TOKEN` in the same environment

**Security considerations:**

- The token is validated using SHA-256 hash comparison to prevent timing attacks
- Authentication failures are logged to stderr (tokens are never logged)
- Since MCP over stdio doesn't support HTTP-style bearer tokens, both client and server must share the same `TENDRIL_MCP_TOKEN` environment variable
- This approach works because Claude Code spawns the MCP server process with the same environment

**Example setup for systemd:**

```ini
[Service]
Environment="TENDRIL_MCP_TOKEN=your-secure-token-here"
```

**Example setup for shell (bash/zsh):**

```bash
export TENDRIL_MCP_TOKEN="your-secure-token-here"
```

### Configuration for Claude Code

Add to `~/.claude/mcp.json`:

```json
{
  "mcpServers": {
    "tendril": {
      "command": "tendril",
      "args": ["mcp"]
    }
  }
}
```

### Implementation

- `Commands/McpCommand.cs` — Command handler that intercepts `tendril mcp` args
- `Mcp/TendrilMcpServer.cs` — Configures and runs the MCP server using the `ModelContextProtocol` SDK
- `Mcp/Tools/PlanTools.cs` — Tool definitions for plan queries and inbox creation

The MCP server reads plans directly from the filesystem via `TENDRIL_HOME/Plans/` and writes inbox items to `TENDRIL_HOME/Inbox/`. It does not require the Tendril web server to be running.

## Debugging Plans on a Dev Machine

### Key Directories

All paths resolve from environment variables — check these first:

| Variable | Purpose | Fallback |
|----------|---------|----------|
| `TENDRIL_HOME` | Config, database, hooks, trash, inbox | Required — onboarding triggers if unset |
| `TENDRIL_PLANS` | Plans directory (overrides `TENDRIL_HOME/Plans`) | `TENDRIL_HOME/Plans` |
| `REPOS_HOME` | Base path for `%REPOS_HOME%` expansion in config.yaml repo paths | None (optional) |

Typical layout on a dev machine:

```
$TENDRIL_HOME/
  config.yaml          # Project definitions, verifications, coding agents
  tendril.db           # SQLite — plan metadata cache (rebuilt from filesystem on startup)
  Plans/ or $TENDRIL_PLANS/
    .counter           # Next plan ID (integer, incremented atomically)
    03450-SomePlan/
      plan.yaml        # Plan metadata (state, repos, commits, PRs)
      revisions/       # 001.md, 002.md — plan revision history
      logs/            # Per-plan execution logs
      verification/    # Verification reports
      artifacts/       # Build artifacts, screenshots
      worktrees/       # Git worktree paths used during execution
  Trash/               # Deleted/duplicate plans (PlanId-Title.md)
  Inbox/               # Incoming plan requests (.md files, picked up by InboxWatcherService)
  Logs/Jobs/           # Raw output for failed/timed-out jobs without a plan folder
  Hooks/               # After-hooks (e.g., SlackNotify)
```

### Promptware Logs

Each promptware keeps its own logs in `Promptwares/<Type>/Logs/`:

- `{PlanId}.md` — Agent's execution summary (outcome, analysis, tools/memory changed)
- `{PlanId}.raw.jsonl` — Full stream-json output from the Claude session

These are the primary debugging artifacts. The `.md` log tells you what the agent decided; the `.raw.jsonl` has every tool call, thinking block, and API response.

### Job Lifecycle and Failure Modes

Jobs flow through: `Pending → Queued → Running → Completed/Failed/Timeout/Stopped`

**CreatePlan verification** (`VerifyCreatePlanResult` in `JobService.cs`) runs after the agent exits with code 0 and can **change Completed → Failed** if:

1. Agent output doesn't contain `"Plan created: <folder>"` marker
2. No plan folder matching `AllocatedPlanId` exists on disk (`FindPlanFolderById`)
3. No trash entry for that ID exists either (`FindTrashEntryById`)

When debugging a failed CreatePlan, check in order:
1. Does the plan folder exist in `$TENDRIL_PLANS/{PlanId}-*`?
2. Does a trash entry exist in `$TENDRIL_HOME/Trash/{PlanId}-*.md`?
3. Read the promptware log `Promptwares/CreatePlan/Logs/{PlanId}.md`
4. Read the raw output `Promptwares/CreatePlan/Logs/{PlanId}.raw.jsonl`
5. Check `$TENDRIL_HOME/Logs/Jobs/logs/` for failed job output dumps

### CLI Commands

Tendril exposes CLI commands via Spectre.Console.Cli for debugging:

```bash
tendril doctor              # Run system diagnostics
tendril doctor plans        # Check plan health (--all, --fix)
tendril db-version          # Show database schema version
tendril db-migrate          # Apply pending migrations
tendril db-reset --force    # Reset database (rebuilds from filesystem)
tendril plan list           # List plans with filtering
tendril plan get <id>       # Show plan details
```

### Common Issues

- **Plan counter collisions**: `$TENDRIL_PLANS/.counter` is protected by an in-process lock. If plans get duplicate IDs, check that only one Tendril instance is running.
- **Plans not appearing in UI**: Run `tendril doctor plans` to check for malformed `plan.yaml` files. `PlanReaderService.RepairPlans()` runs on startup but silently skips plans it can't fix.
- **Build errors from locked files**: Tendril locks its own exe while running. Use `--no-dependencies` or stop the running instance before building.
- **Missing `.raw.jsonl` logs**: These are written by `WriteRawOutputLog` in `JobService.cs` on job completion. If the Logs directory doesn't exist for a promptware, no raw log is written.
