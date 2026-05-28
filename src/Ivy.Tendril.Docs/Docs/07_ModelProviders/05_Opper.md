---
icon: Server
searchHints:
  - opper
  - gateway
  - eu
  - multi-provider
---

# Opper.ai

<Ingress>
AI gateway providing access to 300+ models from Anthropic, OpenAI, Google, and open-weight providers with EU data residency options.
</Ingress>

## Setup

1. Install the Opper CLI (requires Node 20.12+):
   ```bash
   npm i -g @opperai/cli
   ```
2. Sign in via OAuth:
   ```bash
   opper login
   ```
3. Launch OpenCode through Opper:
   ```bash
   opper launch opencode
   ```

No separate API keys are needed. Authentication is handled by the Opper CLI.

## Switching Models

Use the `--model` flag to select a specific model:

```bash
opper launch opencode --model anthropic/claude-opus-4-7
```

## Using with Tendril

Set OpenCode as your coding agent in `config.yaml`:

```yaml
codingAgent: opencode
```

## Links

- [Opper + OpenCode docs](https://opper.ai/agent-cli)
