# Manual Agent Testing Guide

This guide describes how to manually test agent CLI integration with Tendril.

## Prerequisites

### Supported Agents

- **Claude** (`claude`) — Anthropic Claude Code
- **Codex** (`codex`) — OpenAI Codex CLI
- **Gemini CLI** (`gemini`) — Gemini CLI
- **Copilot** (`copilot` or `gh copilot`) — GitHub Copilot CLI
- **OpenCode** (`opencode`) — OpenCode CLI

### Verifying Installation

```bash
# Verify each agent is on PATH
claude --version
codex --version
agy --version
copilot --version
opencode --version
```

## Running Integration Tests

The E2E integration tests in `Ivy.Tendril.Agents.Test.End2End` require agent CLIs to be installed. To run them:

```bash
dotnet test src/Ivy.Tendril.Agents.Test.End2End/ --filter "FullyQualifiedName~HealthCheck"
```

## Manual Testing Procedure

### Testing Claude

```bash
mkdir /tmp/claude-test && cd /tmp/claude-test
claude --dangerously-skip-permissions "Create a file called hello.txt with the content 'Hello from Claude'"
cat hello.txt
```

### Testing Codex

```bash
mkdir /tmp/codex-test && cd /tmp/codex-test
codex --full-auto "Create a file called hello.txt with the content 'Hello from Codex'"
cat hello.txt
```

## Expected Outcomes

### Successful Execution

- The agent CLI process should exit with code 0
- The requested file should be created in the working directory
- The file content should match the prompt instructions

### Common Issues

1. **CLI not found**: Ensure the agent CLI is installed and on your PATH
2. **Permission denied**: Check file system permissions in the test directory
3. **Timeout**: Some agents may take longer for first run (downloading models, etc.)

## Known Limitations

- **CI Environment**: These tests are not run in CI by default because agent CLIs may not be available
- **API Keys**: Some agents require API keys or authentication
- **Network Access**: First run may require internet access
- **Platform Support**: CLI availability and behavior may vary by platform

## Integration with Tendril

When agents are used via Tendril's job system:

1. Tendril resolves the agent configuration from `config.yaml`
2. `AgentProviderFactory.Resolve()` selects the CLI via `IAgentRunner`
3. The CLI's `BuildProcessSpec()` method constructs the invocation
4. Tendril launches the process and streams output through `IEventParser`
5. Output is normalized into EventWire format for the frontend

See `AgentProviderFactoryTests.cs` for unit tests of the resolution flow.
