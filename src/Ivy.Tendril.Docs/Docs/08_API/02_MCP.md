---
icon: Bot
searchHints:
  - mcp
  - model context protocol
  - claude
  - tools
  - tendril_get_plan
  - tendril_list_plans
  - tendril_plan_set
---

<Text Color="Green" Small Bold>API</Text>

# MCP Server

<Ingress>
Tendril includes a Model Context Protocol (MCP) server that exposes plan management tools to AI coding agents like Claude Code.
</Ingress>

## Starting the MCP Server

```bash
tendril mcp
```

This launches the MCP server via stdio transport, suitable for use in Claude Code's MCP configuration.

## Authentication

Set the `TENDRIL_MCP_TOKEN` environment variable to require a token for all MCP tool calls. When set, the agent must provide the matching token. When unset, all calls are allowed.

## Available Tools

All tools are prefixed with `tendril_` and provide the same capabilities as the CLI and REST API.

### Plan CRUD

| Tool | Description |
|------|-------------|
| `tendril_get_plan` | Get plan details (full or single field) |
| `tendril_list_plans` | List plans with optional state/project filters |
| `tendril_inbox` | Submit a new plan to the inbox |
| `tendril_plan_set` | Update a plan field (state, title, project, level, priority, etc.) |

### Repositories & Artifacts

| Tool | Description |
|------|-------------|
| `tendril_plan_add_repo` | Add a repository path to a plan |
| `tendril_plan_remove_repo` | Remove a repository from a plan |
| `tendril_plan_add_pr` | Add a PR URL to a plan |
| `tendril_plan_add_commit` | Add a commit SHA to a plan |

### Verifications & Logs

| Tool | Description |
|------|-------------|
| `tendril_plan_set_verification` | Set verification status (Pending/Pass/Fail/Skipped) |
| `tendril_plan_add_log` | Write an execution log entry |

### Recommendations

| Tool | Description |
|------|-------------|
| `tendril_plan_rec_list` | List recommendations (optionally filtered by state) |
| `tendril_plan_rec_add` | Add a recommendation with impact/risk assessment |
| `tendril_plan_rec_accept` | Accept a recommendation (with optional notes) |
| `tendril_plan_rec_decline` | Decline a recommendation (with optional reason) |
| `tendril_plan_rec_remove` | Remove a recommendation |

## Claude Code Configuration

Add Tendril's MCP server to your Claude Code settings (`.claude/settings.json` or project-level):

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

With authentication:

```json
{
  "mcpServers": {
    "tendril": {
      "command": "tendril",
      "args": ["mcp"],
      "env": {
        "TENDRIL_MCP_TOKEN": "your-secret-token"
      }
    }
  }
}
```

## Parity

The MCP tools, REST API, and CLI all operate on the same plan data and share the same validation logic. Changes made through any interface are immediately visible to the others.
