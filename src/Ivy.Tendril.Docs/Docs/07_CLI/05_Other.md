---
icon: Wrench
searchHints:
  - mcp
  - hash
  - password
  - promptwares
  - version
  - run
  - server
---

<Text Color="Green" Small Bold>CLI</Text>

# Other Commands

<Ingress>
Additional utilities for running the server, MCP integration, security, and maintenance.
</Ingress>

## run

```bash
tendril run [--port <port>]
```

Starts the Tendril web server. Automatically applies pending database migrations before serving.

| Option | Effect |
|--------|--------|
| `--port` | Override the default listening port (5010) |

## version

```bash
tendril version
```

Prints the installed Tendril version (e.g. `1.0.18`).

## mcp

```bash
tendril mcp [args...]
```

Launches the Tendril MCP (Model Context Protocol) server for integration with AI coding agents like Claude Code. Additional arguments are forwarded to the MCP runtime. See [MCP Server](../08_API/02_MCP.md) for available tools and configuration.

## hash-password

```bash
tendril hash-password <password> [secret]
```

Generates an Argon2id hash for use in Tendril's authentication system. If `[secret]` is omitted, a random one is generated and printed alongside the hash.

## update-promptwares

```bash
tendril update-promptwares
```

Refreshes the embedded promptware templates from the bundled source. Use after upgrading Tendril to pick up new or updated promptwares.
