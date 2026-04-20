---
icon: Terminal
searchHints:
  - cli
  - command
  - terminal
  - tendril
  - shell
---

<Text Color="Green" Small Bold>CLI</Text>

# Command-Line Interface

<Ingress>
Tendril ships as a single `tendril` binary that doubles as both a web server and a CLI tool for managing plans, databases, diagnostics, and integrations.
</Ingress>

## Usage

```bash
tendril [command] [options]
```

When invoked without a recognized command, Tendril starts the web server on `https://localhost:5010`.

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
