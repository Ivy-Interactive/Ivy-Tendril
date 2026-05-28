---
icon: Server
searchHints:
  - cloudflare
  - workers ai
  - cf
  - edge
---

# Cloudflare

<Ingress>
Use Cloudflare Workers AI as a model provider and connect to Cloudflare MCP servers for building and deploying Workers.
</Ingress>

## Setup

1. Launch OpenCode and connect to Cloudflare Workers AI:
   ```bash
   opencode
   ```
   Then type `/connect` and select **Cloudflare**.
2. Authorize via OAuth when prompted on first use.

## Cloudflare Skills (Optional)

Install Cloudflare MCP skills for Workers development:

```bash
npx skills add https://github.com/cloudflare/skills
```

This adds MCP servers for managing Workers, bindings, builds, and observability.

## Using with Tendril

Set OpenCode as your coding agent in `config.yaml`:

```yaml
codingAgent: opencode
```

## Links

- [Cloudflare + OpenCode docs](https://developers.cloudflare.com/agent-setup/opencode/)
