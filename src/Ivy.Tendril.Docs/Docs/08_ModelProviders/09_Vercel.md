---
icon: Server
searchHints:
  - vercel
  - ai gateway
  - unified
  - observability
---

# Vercel AI Gateway

<Ingress>
Route requests through Vercel's AI Gateway for unified access to OpenAI, Anthropic, Google, xAI, and more with built-in observability.
</Ingress>

## Setup

1. Create an API key in the [Vercel AI Gateway dashboard](https://vercel.com/dashboard) under **AI Gateway > API keys**
2. Launch OpenCode and connect:
   ```bash
   opencode
   ```
   Then type `/connect`, search for **Vercel AI Gateway**, and enter your API key.
3. Select a model with `/models`

## Configuration (Optional)

Customize provider routing in `opencode.json`:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "provider": {
    "vercel": {
      "models": {
        "anthropic/claude-sonnet-4.6": {
          "options": {
            "order": ["anthropic", "vertex"]
          }
        }
      }
    }
  }
}
```

## Monitoring

View usage, spend, and request activity in the Vercel dashboard under **AI Gateway**.

## Using with Tendril

Select OpenCode as your coding agent during onboarding, or under **Settings > Configuration > Coding Agent**.

## Links

- [Vercel + OpenCode docs](https://vercel.com/docs/ai-gateway/coding-agents/opencode)
