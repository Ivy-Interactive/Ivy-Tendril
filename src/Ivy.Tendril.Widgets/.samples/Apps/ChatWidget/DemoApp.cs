using System.Text.Json;
using Ivy;
using Ivy.Tendril.Widgets;
using ChatWidgetControl = Ivy.Tendril.Widgets.ChatWidget;

namespace WidgetSamples.Apps.ChatWidget;

/// <summary>The chat redesign with a mocked conversation: attachments, tool calls, a job event, a questions block, spawned jobs and a live stream.</summary>
[App(title: "Chat", icon: Icons.MessageSquare, group: ["ChatWidget"])]
class DemoApp : ViewBase
{
    private const string FullSessionId = "sess-full";
    private const string EmptySessionId = "sess-empty";

    private static readonly JsonSerializerOptions WireOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private static string Jsonl(params object[] events) =>
        string.Join("\n", events.Select(e => JsonSerializer.Serialize(e, WireOptions)));

    private static readonly string FinishedTurn = Jsonl(
        new { kind = "session_init", timestamp = "2026-09-08T09:00:00Z", session_id = "s1", model = "fable-5-1" },
        new { kind = "thinking", timestamp = "2026-09-08T09:00:01Z", content = "The vault theme settings page lives under Apps/Settings; the dashboard already defines tdb- tokens for light and dark." },
        new { kind = "tool_call", timestamp = "2026-09-08T09:00:02Z", tool_use_id = "tu1", tool_name = "Grep", input = new { pattern = "tdb-", path = "src" } },
        new { kind = "tool_result", timestamp = "2026-09-08T09:00:03Z", tool_use_id = "tu1", output = "src/Ivy.Tendril.Widgets/frontend/src/TendrilDashboard/dashboard.css:7:  --tdb-bg: var(--background, #ffffff);", is_error = false },
        new { kind = "tool_call", timestamp = "2026-09-08T09:00:04Z", tool_use_id = "tu2", tool_name = "Read", input = new { file_path = "src/Ivy.Tendril/Apps/Settings/VaultSetupView.cs" } },
        new { kind = "tool_result", timestamp = "2026-09-08T09:00:05Z", tool_use_id = "tu2", output = "public class VaultSetupView : ViewBase\n{\n    public override object Build() { ... }\n}", is_error = false },
        new { kind = "tool_call", timestamp = "2026-09-08T09:00:06Z", tool_use_id = "tu3", tool_name = "Bash", input = new { command = "tendril job start --plan 00059 --chat-session sess-full", description = "Start plan 00059" } },
        new { kind = "tool_result", timestamp = "2026-09-08T09:00:07Z", tool_use_id = "tu3", output = "Job 00148 started for plan 00059", is_error = false },
        new { kind = "text", timestamp = "2026-09-08T09:00:08Z", text = "Plan 00059 started. The dark mode toggle is in and using the existing tdb- tokens, so it should pick up light/dark theming automatically.", delta = false },
        new
        {
            kind = "result",
            timestamp = "2026-09-08T09:02:05Z",
            response = "Plan 00059 started. The dark mode toggle is in and using the existing tdb- tokens, so it should pick up light/dark theming automatically.",
            is_success = true,
            duration_ms = 125200,
            usage = new { input_tokens = 140284, output_tokens = 23009, cache_read_tokens = 0, cache_write_tokens = 0, reasoning_tokens = 0, cost_usd = 0.42 }
        });

    private static readonly string LiveTurn = Jsonl(
        new { kind = "session_init", timestamp = "2026-09-08T09:10:00Z", session_id = "s2", model = "fable-5-1" },
        new { kind = "tool_call", timestamp = "2026-09-08T09:10:01Z", tool_use_id = "tu9", tool_name = "Read", input = new { file_path = "src/Ivy.Tendril/Apps/Settings/SettingsApp.cs" } },
        new { kind = "tool_result", timestamp = "2026-09-08T09:10:02Z", tool_use_id = "tu9", output = "...", is_error = false },
        new { kind = "text", timestamp = "2026-09-08T09:10:03Z", text = "Opening a pull request against development with the toggle and its tests.", delta = false },
        new { kind = "tool_call", timestamp = "2026-09-08T09:10:04Z", tool_use_id = "tu10", tool_name = "Bash", input = new { command = "gh pr create --fill", description = "Create the pull request" } });

    private const string CompletedMessage = """
        Plan 00059 has been successfully completed: the dark mode toggle is now implemented and utilizes the existing tdb- tokens, ensuring automatic adaptation to light and dark themes. Would you like me to initiate a pull request, or would you prefer to review the changes first?

        ```questions
        - id: proceed
          title: How should we proceed?
          other: false
          options:
            - title: Open a PR
              description: Open a new Pull Request against development branch.
              value: pr
            - title: Review the diff first
              description: Stop the process and wait for my review first.
              value: review
        ```
        """;

    private static List<ChatSessionDto> BuildSessions(string mockupPath)
    {
        var jobs = new List<ChatJobDto>
        {
            new("00148", "ExecutePlan", "Completed", "00059", "Add dark mode toggle to vault theme settings", "Completed successfully"),
            new("00149", "CreatePr", "Completed", "00059", "Add dark mode toggle to vault theme settings", "PR #2431 opened"),
            new("00150", "ExecutePlan", "Completed", "00060", "Persist theme choice per user", "Completed successfully"),
        };

        var full = new ChatSessionDto(
            FullSessionId,
            "Dark mode toggle for vault settings",
            "claude",
            "fable-5-1",
            "2026-09-08T08:59:00Z",
            "2026-09-08T09:12:00Z",
            [
                new ChatMessageDto("m1", "user", $"Add a dark mode toggle to the vault theme settings page\n\n[Attached Files]:\n- {mockupPath}\n- /tmp/design-notes.md", "9:00 AM", "claude", "fable-5-1"),
                new ChatMessageDto("m2", "assistant", "Plan 00059 started.", "9:02 AM", "claude", "fable-5-1", FinishedTurn),
                new ChatMessageDto("m3", "system", "[System Event] Job 00148 (ExecutePlan) for '00059: Add dark mode toggle to vault theme settings' has finished with status: Completed (Completed successfully). Please inspect the outcome, determine whether any action is needed or if any issues occurred, and proactively guide the user on the results and next steps.", "9:11 AM"),
                new ChatMessageDto("m4", "assistant", CompletedMessage, "9:12 AM", "claude", "fable-5-1"),
            ],
            Effort: "max",
            SpawnedJobs: jobs);

        var empty = new ChatSessionDto(EmptySessionId, "New Chat", "claude", "fable-5-1", "2026-09-08T09:20:00Z", "2026-09-08T09:20:00Z", []);

        return [full, empty];
    }

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var streaming = UseState(false);
        var activeId = UseState(FullSessionId);
        var selectedAgent = UseState("claude");
        var selectedModel = UseState("fable-5-1");
        var selectedEffort = UseState("max");

        // src/logo.png, resolved from the build output so the demo does not depend on the working directory.
        var mockupPath = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "logo.png"));

        var claudeModels = new List<ModelOptionDto> { new("fable-5-1", "Fable 5.1"), new("opus-5", "Opus 5"), new("sonnet-5", "Sonnet 5") };
        var agents = new List<AgentOptionDto>
        {
            new("claude", "Claude Code", "ClaudeCode", claudeModels, SupportsEffort: true),
            new("codex", "ChatGPT", "OpenAI", [new("gpt-5-6", "GPT-5.6"), new("gpt-5-6-mini", "GPT-5.6 mini")]),
            new("grok", "Grok Build", "Terminal", [new("grok-5", "Grok 5")]),
            new("gemini", "Gemini CLI", "Gemini", [new("gemini-3-8-pro", "Gemini 3.8 Pro"), new("gemini-3-8-flash", "Gemini 3.8 Flash")]),
        };
        var models = agents.First(a => a.Id == selectedAgent.Value).Models ?? [];
        var efforts = new List<EffortOptionDto> { new("default", "Default"), new("low", "Low"), new("medium", "Medium"), new("high", "High"), new("max", "Max") };

        var chat = new ChatWidgetControl
        {
            ActiveSessionId = activeId.Value,
            Sessions = BuildSessions(mockupPath),
            Agents = agents,
            Models = models,
            Efforts = efforts,
            SelectedAgent = selectedAgent.Value,
            SelectedModel = selectedModel.Value,
            SelectedEffort = selectedEffort.Value,
            SupportsEffort = selectedAgent.Value == "claude",
            IsStreaming = streaming.Value,
            StreamingText = streaming.Value ? LiveTurn : null,
            Greeting = "Good Evening, Joel!",
            Headline = "What Are We Producing Today?",
            OnSendMessage = e => { client.Toast(e.Value.Prompt, "OnSendMessage").Info(); return ValueTask.CompletedTask; },
            OnCancelStream = _ => { streaming.Set(false); client.Toast("Stream cancelled", "OnCancelStream").Info(); return ValueTask.CompletedTask; },
            OnCreateSession = _ => { activeId.Set(EmptySessionId); client.Toast("New chat", "OnCreateSession").Info(); return ValueTask.CompletedTask; },
            OnDeleteSession = e => { client.Toast(e.Value, "OnDeleteSession").Warning(); return ValueTask.CompletedTask; },
            OnRenameSession = e => { client.Toast(string.Join(" -> ", e.Value), "OnRenameSession").Info(); return ValueTask.CompletedTask; },
            OnAgentChanged = e =>
            {
                selectedAgent.Set(e.Value);
                selectedModel.Set(agents.First(a => a.Id == e.Value).Models?[0].Id ?? "default");
                selectedEffort.Set("default");
                return ValueTask.CompletedTask;
            },
            OnModelChanged = e => { selectedModel.Set(e.Value); return ValueTask.CompletedTask; },
            OnEffortChanged = e => { selectedEffort.Set(e.Value); return ValueTask.CompletedTask; },
            OnAnswerQuestion = e => { client.Toast(e.Value.ResponseText, "OnAnswerQuestion").Success(); return ValueTask.CompletedTask; },
            OnOpenPlan = e => { client.Toast($"Plan {e.Value}", "OnOpenPlan").Info(); return ValueTask.CompletedTask; },
        }
        .WithLayout()
        .Full()
        .RemoveParentPadding();

        // Demo-only toggles for the states the mocked backend cannot drive on its own.
        var toggles = new FloatingPanel(
                Layout.Horizontal().Gap(2)
                | new Button($"Streaming: {(streaming.Value ? "on" : "off")}",
                    () => streaming.Set(!streaming.Value)).Small().Variant(ButtonVariant.Secondary)
                | new Button(activeId.Value == FullSessionId ? "Show empty chat" : "Show conversation",
                    () => activeId.Set(activeId.Value == FullSessionId ? EmptySessionId : FullSessionId)).Small().Variant(ButtonVariant.Secondary))
            .Offset(new Thickness(0, 0, 8, 8));

        return new Fragment(chat, toggles);
    }
}
