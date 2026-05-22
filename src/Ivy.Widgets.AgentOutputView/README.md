# Ivy.Widgets.AgentOutputView

Terminal-themed agent output view for Ivy applications. Renders a universal EventWire JSON stream from any supported coding agent (Claude, Codex, Copilot, Gemini, OpenCode).

## Usage

```csharp
new AgentOutputView()
    .JsonStream(preBufferedOutput)
    .Stream(liveOutputStream)
    .AutoScroll(true)
    .ShowStatusLabel(true)
    .Height(Size.Full());
```

## EventWire Format

The widget consumes newline-delimited JSON where each line has a `kind` field:

- `session_init` — session metadata (model, tools)
- `text` — assistant text output (supports delta streaming)
- `thinking` — reasoning/thinking content
- `tool_call` — tool invocation
- `tool_result` — tool execution result
- `result` — session completion summary
- `error` — error events
