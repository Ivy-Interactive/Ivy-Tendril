---
icon: Server
searchHints:
  - model providers
  - providers
  - api
  - inference
  - gateway
  - llm
---

# Model Providers

<Ingress>
OpenCode supports multiple model providers out of the box. Configure a provider to route Tendril's agent execution through the inference backend of your choice.
</Ingress>

## How It Works

1. Set up your chosen provider in OpenCode (authenticate, select models)
2. Select OpenCode as your coding agent in Tendril (`codingAgent: opencode`)
3. Tendril routes all plan execution through OpenCode, which connects to your configured provider

Use `/connect` inside OpenCode to add a provider interactively, or `/models` to switch models at any time.
