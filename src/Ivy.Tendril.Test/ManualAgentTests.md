# Manual Agent Testing Guide

This guide describes how to manually test Codex and Gemini agent integration with Tendril.

## Prerequisites

### Installing Codex CLI

```bash
# Installation instructions for Codex
# (Placeholder - add actual installation instructions)
```

### Installing Gemini CLI

```bash
# Installation instructions for Gemini
# (Placeholder - add actual installation instructions)
```

## Running Integration Tests

The integration tests in `AgentIntegrationTests.cs` are skipped by default because they require the agent CLIs to be installed. To run them:

1. Ensure the agent CLI is installed and available on your PATH:
   ```bash
   # Verify Codex
   which codex  # Unix
   where codex  # Windows
   
   # Verify Gemini
   which gemini  # Unix
   where gemini  # Windows
   ```

2. Remove the `Skip` attribute from the test method you want to run in `AgentIntegrationTests.cs`:
   ```csharp
   // Change from:
   [Fact(Skip = "Requires codex CLI to be installed")]
   
   // To:
   [Fact]
   ```

3. Run the specific test:
   ```bash
   dotnet test --filter "FullyQualifiedName~Ivy.Tendril.Test.Agents.AgentIntegrationTests.Codex_CanExecuteSimplePlan"
   dotnet test --filter "FullyQualifiedName~Ivy.Tendril.Test.Agents.AgentIntegrationTests.Gemini_CanExecuteSimplePlan"
   ```

## Manual Testing Procedure

### Testing Codex

1. Create a test directory:
   ```bash
   mkdir /tmp/codex-test
   cd /tmp/codex-test
   ```

2. Run Codex with a simple prompt:
   ```bash
   codex --full-auto "Create a file called hello.txt with the content 'Hello from Codex'"
   ```

3. Verify the output:
   ```bash
   cat hello.txt
   # Should contain: Hello from Codex
   ```

### Testing Gemini

1. Create a test directory:
   ```bash
   mkdir /tmp/gemini-test
   cd /tmp/gemini-test
   ```

2. Run Gemini with a simple prompt:
   ```bash
   gemini --sandbox --yolo "Create a file called hello.txt with the content 'Hello from Gemini'"
   ```

3. Verify the output:
   ```bash
   cat hello.txt
   # Should contain: Hello from Gemini
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

- **CI Environment**: These tests are not run in CI by default because agent CLIs may not be available in the CI environment
- **API Keys**: Some agents may require API keys or authentication - check agent-specific documentation
- **Network Access**: First run may require internet access to download models or dependencies
- **Platform Support**: CLI availability and behavior may vary by platform (Windows/Linux/macOS)

## Troubleshooting

### Codex Issues

- Check Codex version: `codex --version`
- Review Codex logs (if available)
- Verify model access and permissions

### Gemini Issues

- Check Gemini version: `gemini --version`
- Verify sandbox mode is working: `gemini --sandbox --help`
- Review Gemini configuration

## Integration with Tendril

When these agents are used via Tendril's `AgentProviderFactory`, the following happens:

1. Tendril resolves the agent configuration from `config.yaml`
2. `AgentProviderFactory.Resolve()` selects the appropriate provider
3. The provider's `BuildProcessStart()` method constructs the CLI invocation
4. Tendril launches the process and captures output
5. The provider's `ExtractResult()` method parses the output for results

See `AgentProviderFactoryTests.cs` and `AgentProviderTests.cs` for unit tests of this flow.
