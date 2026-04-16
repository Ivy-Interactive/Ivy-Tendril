# Ivy Tendril — Agent Instructions

## Plan Schema Migrations

When changing the `plan.yaml` structure (adding/removing/renaming fields, changing field types):

1. **Update `Plans.md`** (`Promptwares/.shared/Plans.md`) — this is the source of truth for the plan schema
2. **Add a repair step** in `PlanReaderService.RepairPlans()` — this runs on every Tendril startup and must migrate all existing plans to the new format
3. **Keep `PlanYaml.cs` in sync** — the deserialization model must match what `Plans.md` documents
4. **Update promptware instructions** — any promptware that writes `plan.yaml` (MakePlan, ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan) must produce the new format

Existing plans on disk are never recreated — they must be repaired in place. If `RepairPlans()` can't fix a plan, it will silently fail and that plan won't appear in the UI. Always test your repair logic against real plan files.

## Project Structure

- `Services/` — ConfigService, PlanReaderService, JobService, GitService
- `Apps/` — PlansApp, ReviewApp, JobsApp, IceboxApp, and their views
- `Promptwares/` — MakePlan, ExecutePlan, UpdatePlan, SplitPlan, ExpandPlan, MakePr, IvyFrameworkVerification
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
