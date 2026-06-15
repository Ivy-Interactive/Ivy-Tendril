---
icon: Bot
searchHints:
  - coding agents
  - agent
  - claude
  - codex
  - copilot
  - opencode
  - gemini
---

# Coding Agents

<Ingress>
Coding agents are the AI-powered runtimes that execute Tendril plans. Choose an agent, configure profiles, and let Tendril orchestrate the work.
</Ingress>

## Environment Variables

You can inject environment variables into the coding agent process via `config.yaml`. These are applied to both job execution (plans) and the interactive Agent tab (PTY).

```yaml
codingAgents:
- name: claude
  environmentVariables:
    CLAUDE_CODE_USE_BEDROCK: "1"
    ANTHROPIC_BASE_URL: "https://your-endpoint.example.com"
  profiles:
    - name: balanced
      model: sonnet
      effort: high
```

Any key/value pairs under `environmentVariables` are set in the agent's process environment before it starts. Use this for provider configuration (e.g. Bedrock, custom API endpoints) or any runtime flags the agent CLI supports.
