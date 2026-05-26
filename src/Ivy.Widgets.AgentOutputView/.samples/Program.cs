using Ivy;
using Ivy.Widgets.AgentOutputView;

var server = new Server();
server.UseAppShell();
server.AddApp<PreBufferedDemo>();
server.AddApp<LiveStreamDemo>();
server.AddApp<ErrorDemo>();
await server.RunAsync();

[App(title: "Pre-buffered", icon: Icons.FileText)]
class PreBufferedDemo : ViewBase
{
    record Props(
        bool AutoScroll = false,
        bool ShowThinking = false,
        bool ShowSystemEvents = false,
        bool ShowStatusLabel = true,
        string? StatusLabelOverride = null);

    public override object Build()
    {
        var props = UseState(new Props());

        var view = new AgentOutputView()
            .JsonStream(SampleData.SuccessfulSession)
            .AutoScroll(props.Value.AutoScroll)
            .ShowThinking(props.Value.ShowThinking)
            .ShowSystemEvents(props.Value.ShowSystemEvents)
            .ShowStatusLabel(props.Value.ShowStatusLabel)
            .Height(Size.Full());

        if (!string.IsNullOrWhiteSpace(props.Value.StatusLabelOverride))
            view = view.StatusLabel(props.Value.StatusLabelOverride);

        return new SidebarLayout(
            view,
            props.ToForm("Apply")
        ).Resizable();
    }
}

[App(title: "Live Stream", icon: Icons.Radio)]
class LiveStreamDemo : ViewBase
{
    public override object Build()
    {
        var stream = UseStream<string>();
        var running = UseState(false);

        var button = running.Value
            ? new Button("Running...").Disabled()
            : new Button("Start").Primary().OnClick(async () =>
            {
                running.Set(true);
                foreach (var evt in SampleData.Events)
                {
                    stream.Write(evt);
                    await Task.Delay(600);
                }
                running.Set(false);
            });

        return Layout.Vertical().Height(Size.Full()).Gap(2)
               | button
               | new AgentOutputView()
                   .Stream(stream)
                   .ShowStatusLabel(true)
                   .Height(Size.Full());
    }
}

[App(title: "Error Case", icon: Icons.CircleX)]
class ErrorDemo : ViewBase
{
    public override object Build()
    {
        return new AgentOutputView()
            .JsonStream(SampleData.FailedSession)
            .AutoScroll(false)
            .ShowStatusLabel(false)
            .Height(Size.Full());
    }
}

static class SampleData
{
    public static readonly string[] Events =
    [
        """{"kind":"session_init","timestamp":"2026-05-22T10:00:00Z","session_id":"sess_01","model":"claude-opus-4-6-20250514","tools":["Read","Write","Edit","Bash","Glob","Grep"]}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:01Z","text":"I'll help you fix the authentication middleware. Let me start by reading the current implementation.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:02Z","tool_use_id":"tu_01","tool_name":"Read","input":{"file_path":"src/middleware/auth.ts"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:03Z","tool_use_id":"tu_01","output":"import { NextRequest } from 'next/server';\n\nexport function authMiddleware(req: NextRequest) {\n  const token = req.headers.get('authorization');\n  if (!token) return new Response('Unauthorized', { status: 401 });\n  return null;\n}","is_error":false}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:04Z","text":"I can see the issue — the middleware doesn't validate the token format. Let me check the test file and related config.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:05Z","tool_use_id":"tu_02","tool_name":"Glob","input":{"pattern":"src/**/*auth*test*"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:05Z","tool_use_id":"tu_02","output":"src/middleware/__tests__/auth.test.ts","is_error":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:05Z","tool_use_id":"tu_02b","tool_name":"Read","input":{"file_path":"src/middleware/__tests__/auth.test.ts"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:05Z","tool_use_id":"tu_02b","output":"import { authMiddleware } from '../auth';\n\ndescribe('authMiddleware', () => {\n  it('should reject missing token', () => { ... });\n  it('should reject malformed tokens', () => { ... });\n  it('should accept valid Bearer token', () => { ... });\n  it('should reject expired token', () => { ... });\n});","is_error":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:06Z","tool_use_id":"tu_03","tool_name":"Bash","input":{"command":"grep -n 'Bearer' src/middleware/auth.ts"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:06Z","tool_use_id":"tu_03","output":"(no matches)","is_error":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:06Z","tool_use_id":"tu_03b","tool_name":"Grep","input":{"pattern":"Bearer","path":"src/middleware/__tests__/auth.test.ts"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:06Z","tool_use_id":"tu_03b","output":"3:  const validToken = 'Bearer eyJhbG...';\n7:  expect(authMiddleware(req)).toEqual(null);","is_error":false}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:07Z","text":"The middleware doesn't check for the `Bearer` prefix. I'll fix the validation to properly parse Bearer tokens.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:08Z","tool_use_id":"tu_04","tool_name":"Edit","input":{"file_path":"src/middleware/auth.ts","old_string":"const token = req.headers.get('authorization');\n  if (!token) return new Response('Unauthorized', { status: 401 });","new_string":"const authHeader = req.headers.get('authorization');\n  if (!authHeader?.startsWith('Bearer ')) return new Response('Unauthorized', { status: 401 });\n  const token = authHeader.slice(7);\n  if (!token) return new Response('Unauthorized', { status: 401 });"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:09Z","tool_use_id":"tu_04","output":"File edited successfully.","is_error":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:10Z","tool_use_id":"tu_05","tool_name":"Bash","input":{"command":"npm test -- --testPathPattern=auth 2>&1 | tail -5"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:12Z","tool_use_id":"tu_05","output":"Tests:       3 passed, 1 failed, 4 total\nTime:        1.234s\n\nFAILED: 'should reject malformed tokens' - Expected 401 but got 200","is_error":true}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:13Z","text":"One test is failing — the malformed token case. I need to add additional validation for the token format after extracting it.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:14Z","tool_use_id":"tu_06","tool_name":"Edit","input":{"file_path":"src/middleware/auth.ts","old_string":"const token = authHeader.slice(7);\n  if (!token) return new Response('Unauthorized', { status: 401 });","new_string":"const token = authHeader.slice(7).trim();\n  if (!token || token.split('.').length !== 3) return new Response('Unauthorized', { status: 401 });"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:14Z","tool_use_id":"tu_06","output":"File edited successfully.","is_error":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:15Z","tool_use_id":"tu_07","tool_name":"Bash","input":{"command":"npm test -- --testPathPattern=auth 2>&1 | tail -3"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:17Z","tool_use_id":"tu_07","output":"Tests:       4 passed, 4 total\nTime:        1.102s\nRan all test suites matching /auth/i.","is_error":false}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:18Z","text":"All 4 tests pass now. The fix validates both the Bearer prefix and the JWT structure (three dot-separated segments).","delta":false}""",
        """{"kind":"result","timestamp":"2026-05-22T10:00:18Z","response":"Fixed the auth middleware to properly validate Bearer tokens. The middleware now checks for the Bearer prefix and validates the token has a valid JWT structure (three dot-separated segments). All 4 tests pass.","is_success":true,"duration_ms":18000,"turn_count":7,"usage":{"input_tokens":12450,"output_tokens":3200,"cache_read_tokens":8000,"cache_write_tokens":1200,"reasoning_tokens":0,"cost_usd":0.0842}}""",
    ];

    public static string SuccessfulSession => string.Join("\n", Events);

    public static string FailedSession => string.Join("\n",
        """{"kind":"session_init","timestamp":"2026-05-22T10:00:00Z","session_id":"sess_02","model":"claude-opus-4-6-20250514","tools":["Read","Write","Edit","Bash"]}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:01Z","text":"Let me deploy the latest changes to production.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:02Z","tool_use_id":"tu_10","tool_name":"Bash","input":{"command":"git push origin main"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:04Z","tool_use_id":"tu_10","output":"error: failed to push some refs to 'origin'\nhint: Updates were rejected because the remote contains work that you do not have locally.","is_error":true}""",
        """{"kind":"text","timestamp":"2026-05-22T10:00:05Z","text":"The push failed because the remote has diverged. Let me pull and rebase.","delta":false}""",
        """{"kind":"tool_call","timestamp":"2026-05-22T10:00:06Z","tool_use_id":"tu_11","tool_name":"Bash","input":{"command":"git pull --rebase origin main"}}""",
        """{"kind":"tool_result","timestamp":"2026-05-22T10:00:08Z","tool_use_id":"tu_11","output":"CONFLICT (content): Merge conflict in src/index.ts\nerror: could not apply abc1234... feat: add auth\nhint: Resolve all conflicts manually, mark them as resolved with git add","is_error":true}""",
        """{"kind":"error","timestamp":"2026-05-22T10:00:09Z","message":"Rebase failed with merge conflicts that require manual resolution. Cannot proceed automatically.","code":"MERGE_CONFLICT","is_retryable":false,"is_auth_error":false}""",
        """{"kind":"result","timestamp":"2026-05-22T10:00:09Z","response":"Failed to deploy: merge conflicts in src/index.ts need manual resolution.","is_success":false,"duration_ms":9000,"turn_count":3,"usage":{"input_tokens":4200,"output_tokens":890,"cache_read_tokens":2000,"cache_write_tokens":400,"reasoning_tokens":0,"cost_usd":0.0156}}"""
    );
}
