# Ivy.Tendril.Agents

Cross-platform .NET 10 library for orchestrating coding agent CLIs. Provides a unified API to spawn, stream events from, and manage sessions with Claude Code, GitHub Copilot, Codex, Gemini, and OpenCode.

## Architecture

```
Ivy.Tendril.Agents/
├── Abstractions/          # Public contracts (interfaces, records, enums)
├── Providers/
│   ├── Claude/            # Claude Code CLI integration
│   ├── Codex/             # Codex CLI integration
│   ├── Copilot/           # GitHub Copilot CLI integration
│   ├── Gemini/            # Gemini CLI integration
│   └── OpenCode/          # OpenCode CLI integration
├── Helpers/               # Shared utilities (process spawning, binary resolution)
└── Runtime/               # Core runtime (AgentSession, AgentRunner)
```

## Key Abstractions

| Interface | Purpose |
|-----------|---------|
| `IAgentDescriptor` | Base: capabilities, tool name translation, environment |
| `IAgentCli` | Builds process specs for a CLI-based agent (extends IAgentDescriptor) |
| `IAgentPty` | Builds PTY specs for interactive terminal sessions |
| `IAgentProcess` | Testable process abstraction (stdin/stdout/lifecycle) |
| `IEventParser` | Parses agent-specific JSONL output into `AgentEvent` records |
| `IFailureAnalyzer` | Classifies failures into retryable categories |
| `IAgentHealthCheck` | Install status, auth, version, model validation, onboarding |
| `IAgentSession` | Running session: observable events, state, permissions, multi-turn |
| `IAgentRunner` | Orchestrator: launches sessions, manages registrations |
| `IInteractionHandler` | Runtime permission/question handling |
| `IRetryPolicy` | Retry decisions with fallback agent support |
| `ISessionCostParser` | Parse session files for offline cost analysis |
| `IModelPricingProvider` | Model pricing data for cost calculation |
| `IEventSerializer` | Serialize/deserialize events to wire format |

## Provider Contract

Each agent provider implements three classes:

1. **`{Agent}Cli : IAgentCli`** — Maps `AgentLaunchConfig` to `AgentProcessSpec` with agent-specific CLI flags, tool name translation, writable directory extraction
2. **`{Agent}EventParser : IEventParser`** — Parses that agent's JSON output format into normalized `AgentEvent` types
3. **`{Agent}HealthCheck : IAgentHealthCheck`** — Verifies binary presence, auth tokens, version, model validation, onboarding info

## Event Model

All agents produce the same normalized event hierarchy:

**Session Lifecycle:**
- `SessionStartingEvent` — Session is being launched (config, transport, metadata)
- `SessionInitEvent` — Agent initialized (session ID, model, available tools)
- `SessionActiveEvent` — First real event received
- `SessionCompletedEvent` — Session finished (final state, result, duration)
- `IdleTimeoutEvent` — Agent went idle beyond threshold
- `RetryEvent` — Retrying after failure

**Content:**
- `TextEvent` — Text output (supports delta streaming)
- `ThinkingEvent` — Internal reasoning (when available)
- `ToolCallEvent` — Agent invoked a tool (name, input JSON)
- `ToolResultEvent` — Tool execution result

**Interaction:**
- `PermissionRequestEvent` — Agent needs permission for an action
- `PermissionDenialEvent` — Permission was denied
- `UserQuestionEvent` — Agent asking the user a question

**Terminal:**
- `ResultEvent` — Final result with usage, cost, duration, exit code
- `ErrorEvent` — Errors (retryable flag, auth error flag)
- `FileChangeEvent` — File was created/modified/deleted
- `StderrEvent` — Raw stderr output
- `SystemEvent` — Agent system messages

## Usage

```csharp
var runner = new AgentRunner();
runner.Register(new ClaudeCli(), new ClaudeEventParser(), new ClaudeHealthCheck());

// Simple: run to completion
var result = await runner.RunToCompletionAsync(new AgentResolutionContext
{
    AgentId = AgentId.Claude,
    Prompt = "Explain this function",
    WorkingDirectory = "/path/to/repo",
    MaxTurns = 3,
});

Console.WriteLine(result.Response);
Console.WriteLine($"Cost: ${result.Usage?.CostUsd}, Tokens: {result.Usage?.OutputTokens}");

// Advanced: streaming events with typed handlers
await using var session = await runner.LaunchAsync(context);
session.Events.OnText(e => Console.Write(e.Text));
session.Events.OnToolCall(e => Console.WriteLine($"[{e.ToolName}]"));
session.Events.OnFileChange(e => Console.WriteLine($"  {e.ChangeKind}: {e.FilePath}"));
var finalResult = await session.WaitForCompletionAsync();

// With policies and metadata
var context = new AgentResolutionContext
{
    AgentId = AgentId.Claude,
    Prompt = prompt,
    WorkingDirectory = repoPath,
    TimeoutPolicy = TimeoutPolicy.Default,
    InteractionHandler = AutoApproveHandler.Instance,
    Metadata = new SessionMetadata { JobId = "ci-123", Branch = "feature/x" },
    RecordingBasePath = "/var/log/tendril",
};
```

## Resolution Context

`AgentResolutionContext` is the primary launch request type, supporting:
- Agent selection (or auto-resolution via profiles)
- Model/effort overrides
- Permission mode and tool allowlisting
- Timeout and retry policies
- Interaction handlers for permissions/questions
- Session metadata for tracking
- Recording paths for audit trails
- MCP server configuration
- Budget limits

## Process Model

1. `IAgentCli.BuildProcessSpec()` produces an `AgentProcessSpec` (filename, args, env, stdin content)
2. `ProcessRunner.StartProcess()` spawns the child process
3. Stdin content is written and closed (for stdin-based agents)
4. Stdout lines are read via `IAsyncEnumerable<string>`
5. Each line is passed to `IEventParser.ParseLine()` producing `AgentEvent` records
6. Events are published to a `ReplaySubject<AgentEvent>` (observable)
7. On process exit, `IEventParser.BuildResult()` produces the final `ResultEvent`

## Interaction Model

For agents that support interactive permissions:
- `IInteractionHandler` receives permission requests and user questions
- `AutoApproveHandler` grants all permissions (for CI/automation)
- `PassthroughHandler` returns null (for manual handling)
- Session exposes `RespondToPermissionAsync` / `RespondToQuestionAsync`

## Cross-Platform Considerations

- `BinaryResolver` checks PATH with platform-appropriate extensions (.cmd/.exe/.bat on Windows)
- `ProcessRunner.SendInterrupt()` uses SIGINT on Unix, KillProcessTree on Windows
- All process I/O uses UTF-8 encoding
- Environment variables `CI=true` and `TERM=dumb` suppress interactive prompts

## Wire Format

`AgentEventSchema.cs` defines a complete wire format for serializing events:
- Each event type has a corresponding `*Wire` record
- `UsageWire` supports model breakdowns for multi-model sessions
- Timestamps use ISO 8601 strings
- Used by `SessionRecording` for audit trails and replay

## Testing

E2E tests in `Ivy.Tendril.Agents.Test.End2End` run against real authenticated CLIs:

```bash
dotnet test
```

Tests require the relevant CLI to be installed and authenticated on the machine.

## Adding a New Agent Provider

1. Create `Providers/{Agent}/{Agent}Cli.cs` implementing `IAgentCli`
   - Include tool name translation (`TranslateToolName` / `ReverseTranslateToolName`)
   - Declare capabilities and supported transports
2. Create `Providers/{Agent}/{Agent}EventParser.cs` implementing `IEventParser`
3. Create `Providers/{Agent}/{Agent}HealthCheck.cs` implementing `IAgentHealthCheck`
   - Include `GetOnboardingInfo()` for install/auth guidance
   - Include `ValidateModelAsync()` for model verification
4. Add the agent ID constant to `Abstractions/AgentTypes.cs`
5. Register with `AgentRunner.Register(cli, parser, healthCheck)`
6. Add E2E tests in `Test.End2End/{Agent}/`
