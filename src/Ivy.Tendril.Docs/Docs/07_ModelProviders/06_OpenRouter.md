---
icon: Server
searchHints:
  - openrouter
  - router
  - multi-provider
  - gateway
---

# OpenRouter

<Ingress>
Unified API gateway providing access to models from Anthropic, OpenAI, Google, xAI, Meta, DeepSeek, and more.
</Ingress>

## Setup

1. Get an API key from [openrouter.ai/keys](https://openrouter.ai/keys) (keys start with `sk-or-`)
2. Launch OpenCode and connect:
   ```bash
   opencode
   ```
   Then type `/connect`, select **OpenRouter**, and enter your API key.
3. Select a model with `/models`

## Configuration (Optional)

Add models to `opencode.json` for project-level defaults:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "provider": {
    "openrouter": {
      "models": {
        "~anthropic/claude-sonnet-latest": {},
        "~google/gemini-flash-latest": {}
      }
    }
  }
}
```

## Using with Tendril

Set OpenCode as your coding agent in `config.yaml`:

```yaml
codingAgent: opencode
```

## Links

- [OpenRouter + OpenCode docs](https://openrouter.ai/docs/cookbook/coding-agents/opencode-integration)
