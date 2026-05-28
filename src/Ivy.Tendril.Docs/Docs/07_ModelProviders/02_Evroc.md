---
icon: Server
searchHints:
  - evroc
  - eu
  - european
  - kimi
---

# Evroc

<Ingress>
European sovereign cloud provider with open-source coding models including Kimi, Llama, and Mistral.
</Ingress>

## Setup

1. Create an account at [cloud.evroc.com](https://cloud.evroc.com)
2. Generate an API key in the Console under **Think > Models > + New**
3. Launch OpenCode and connect:
   ```bash
   opencode
   ```
   Then type `/connect`, select **evroc**, and enter your API key.
4. Select a model with `/models`

## Recommended Model

- **moonshotai/Kimi-K2.5** (look for models with the Code tag)

## Using with Tendril

Set OpenCode as your coding agent in `config.yaml`:

```yaml
codingAgent: opencode
```

## Links

- [Evroc + OpenCode docs](https://docs.evroc.com/integrations/opencode.html)
