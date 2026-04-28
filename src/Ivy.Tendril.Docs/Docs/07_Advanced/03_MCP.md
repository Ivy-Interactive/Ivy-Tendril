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

### Plan Query & Creation

| Tool | Parameters | Description |
|------|------------|-------------|
| `tendril_get_plan` | `planId`, `field` (optional) | Get plan details. Returns full plan summary or single field value when `field` is specified. Supported fields: `state`, `project`, `level`, `title`, `created`, `updated`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority`, `repos`, `prs`, `commits`, `verifications`, `dependsOn`, `relatedPlans`, `recommendations` |
| `tendril_list_plans` | `state` (optional), `project` (optional), `since` (optional ISO date) | List plans with optional filters. Returns up to 50 plans. |
| `tendril_inbox` | `title`, `project` (optional), `level` (optional), `prompt` (optional) | Submit a new plan to the inbox. Creates a markdown file in `TENDRIL_HOME/Inbox` for the InboxWatcher to process. |

### Plan Modification

| Tool | Parameters | Description |
|------|------------|-------------|
| `tendril_plan_set` | `planId`, `field`, `value` | Update a scalar field. Supported fields: `state`, `project`, `level`, `title`, `executionProfile`, `initialPrompt`, `sourceUrl`, `priority` |
| `tendril_plan_add_repo` | `planId`, `repoPath` | Add a repository path to a plan |
| `tendril_plan_remove_repo` | `planId`, `repoPath` | Remove a repository from a plan |
| `tendril_plan_add_pr` | `planId`, `prUrl` | Add a PR URL to a plan |
| `tendril_plan_add_commit` | `planId`, `sha` | Add a commit SHA to a plan |
| `tendril_plan_set_verification` | `planId`, `name`, `status` | Set verification status. Valid statuses: `Pending`, `Pass`, `Fail`, `Skipped` |
| `tendril_plan_add_log` | `planId`, `action`, `summary` (optional) | Write an execution log entry |

### Recommendations

| Tool | Parameters | Description |
|------|------------|-------------|
| `tendril_plan_rec_list` | `planId`, `state` (optional) | List recommendations. Filter by state: `Pending`, `Accepted`, `AcceptedWithNotes`, `Declined` |
| `tendril_plan_rec_add` | `planId`, `title`, `description`, `impact` (optional), `risk` (optional) | Add a recommendation. Impact/risk levels: `Small`, `Medium`, `High` |
| `tendril_plan_rec_accept` | `planId`, `title`, `notes` (optional) | Accept a recommendation. Sets state to `Accepted` or `AcceptedWithNotes` if notes provided |
| `tendril_plan_rec_decline` | `planId`, `title`, `reason` (optional) | Decline a recommendation with optional reason |
| `tendril_plan_rec_remove` | `planId`, `title` | Permanently remove a recommendation |

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
