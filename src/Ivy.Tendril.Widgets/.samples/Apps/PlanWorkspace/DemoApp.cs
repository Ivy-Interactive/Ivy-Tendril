using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;
using ChatWidgetControl = Ivy.Tendril.Widgets.ChatWidget;
using PlanWorkspaceControl = Ivy.Tendril.Widgets.PlanWorkspace;

namespace WidgetSamples.Apps.PlanWorkspace;

/// <summary>
///     Every part of the plan page switched on at once: icon actions (active, badged, disabled),
///     overflow menu, the "Update Plan" secondary button, a loading primary, an issue link, review
///     actions above the tabs, badged tabs, the Verifications and Questions dropdowns (with the
///     unread indicator), highlighted annotations in the plan, and a chat with a conversation.
/// </summary>
[App(title: "Showcase", icon: Icons.PanelRight, group: ["PlanWorkspace"])]
class DemoApp : ViewBase
{
    private const string Plan = """
        # Summary

        ## Changes

        Refactor the authentication flow to use a unified modal interface instead of full-page redirects. Add real-time inline validation for email and password fields, and introduce a "show/hide password" toggle to improve user experience. Disable submit buttons during API calls to prevent duplicate submissions.

        ## Rollout

        Ship the modal behind a feature flag first, then enable it for internal accounts for one week before the public rollout.

        ## API Changes

        New and updated module `src/authUtils.js` exports:

        - `validatePassword(password: string): boolean` checks if the password meets minimum complexity requirements (8+ characters, numbers, and symbols)
        - `login(credentials: object): Promise<boolean>` updated to return specific error codes for detailed UI feedback and manage loading states
        - `togglePasswordVisibility(inputElement: HTMLInputElement): void` switches the input type between password and text

        ## Files To Modify

        - `src/components/AuthModal.js` New file: centralized modal component handling both login and signup views
        - `src/styles/auth.css` Added styling for the slide-out modal, inline error text (red), and loading spinners on the submit button
        - `src/main.js` Removed legacy auth page routing; imported AuthModal and wired up open/close toggle triggers from the main navigation
        - `index.html` Added #auth-modal-root div to host the modal, replacing the standalone login page structure

        ## Manual Testing

        1. Open http://localhost:5173 and click the "Sign In" button in the top navigation: the new auth modal should slide into view.
        2. Enter a malformed email address: an "Invalid email format" error message should appear immediately below the field.
        3. Type a password and click the eye icon inside the input field: the password text should become visible. Click it again to mask the text.
        4. Enter correct credentials and click "Login": the button should show a loading state and become disabled.
        5. Wait for the API response: the modal should close automatically, and the main UI should update to show the authenticated user state.

        ```questions
        - id: sessions
          title: Should existing sessions be kept when the modal replaces the login page?
          options:
            - title: Keep them
              value: keep
            - title: Sign everyone out once
              value: reset
        - id: flag
          title: Which feature flag service gates the rollout?
          answer: [launchdarkly]
          options:
            - title: LaunchDarkly
              value: launchdarkly
            - title: Our own flags table
              value: table
        ```
        """;

    private const string SessionId = "sess-plan-59";

    private static readonly List<ChatMessageDto> Conversation =
    [
        new("m1", "user", "Can we keep the existing sessions alive when the modal replaces the login page?", "9:41 AM"),
        new("m2", "assistant",
            "Yes. The modal only swaps the entry point; the session cookie and refresh token flow stay as they are. I would add a note under **Changes** so the executor does not clear sessions by mistake. Want me to update the plan?",
            "9:41 AM"),
        new("m3", "user", "Please do.", "9:42 AM"),
        new("m4", "system", "[System Event] Job 00148 (UpdatePlan) for '00059: Revamp the User Authentication Experience' has finished with status: Completed.", "9:44 AM")
    ];

    private static readonly IEnumerable<ChatJobDto> SpawnedJobs =
    [
        new("00148", "UpdatePlan", "Completed", "00059", "Revamp the User Authentication Experience")
    ];

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var selectedTab = UseState("plan");
        var editing = UseState(false);
        var executing = UseState(false);
        var shareMode = UseState(false);
        var annotations = UseState(() => InitialAnnotations());
        var buildChecked = UseState(true);
        var testChecked = UseState(false);
        var lintChecked = UseState(true);

        UseEffect(() =>
        {
            if (!executing.Value) return System.Reactive.Disposables.Disposable.Empty;
            return new System.Threading.Timer(_ => executing.Set(false), null, 2500, System.Threading.Timeout.Infinite);
        }, executing);

        var actions = shareMode.Value
            ? new List<PlanActionDto>
            {
                new("SharePlan", "Share Plan", nameof(Icons.Share2)),
                new("CopyPlan", "Copy Plan", nameof(Icons.ClipboardCopy))
            }
            : editing.Value
                ? []
                : new List<PlanActionDto>
                {
                    new("Edit", "Edit", nameof(Icons.Pencil), "E"),
                    new("Update", "Update", nameof(Icons.WandSparkles), "U", Active: true),
                    new("Expand", "Expand", nameof(Icons.Expand), "P", Disabled: true),
                    new("Share", "Share", nameof(Icons.Share2), Badge: "2")
                };

        var menu = shareMode.Value || editing.Value
            ? []
            : new List<PlanActionDto>
            {
                new("Split", "Split", nameof(Icons.Scissors), Disabled: true),
                new("Delete", "Delete", nameof(Icons.Trash), "Backspace", Danger: true),
                new("CreateIssue", "Create Issue", nameof(Icons.Github)),
                new("Discuss", "Discuss with Claude Code", "ClaudeCode", FocusChat: true),
                new("OpenInExplorer", "Open in File Manager", nameof(Icons.FolderOpen)),
                new("OpenInTerminal", "Open in Terminal", nameof(Icons.Terminal)),
                new("OpenInEditor", "Open in VS Code", nameof(Icons.Code)),
                new("CopyPath", "Copy Path to Clipboard", nameof(Icons.ClipboardCopy)),
                new("OpenPlanYaml", "Open plan.yaml", nameof(Icons.FileText))
            };

        PlanActionDto? primary = shareMode.Value
            ? null
            : editing.Value
                ? new PlanActionDto("Save", "Save Revision", nameof(Icons.Save), "S")
                : new PlanActionDto("Execute", "Execute", nameof(Icons.Rocket), "x", Loading: executing.Value, Disabled: executing.Value);

        var secondary = shareMode.Value
            ? new List<PlanActionDto>()
            : editing.Value
                ? [new PlanActionDto("Cancel", "Cancel", null, "Escape")]
                : [new PlanActionDto("UpdatePlan", "Update Plan", nameof(Icons.WandSparkles), Badge: (annotations.Value.Count + 1).ToString())];

        var tabs = new List<PlanTabDto>
        {
            new("summary", "Summary"),
            new("plan", "Plan"),
            new("details", "Details"),
            new("git", "Git", "3"),
            new("changes", "Changes", "12"),
            new("artifacts", "Artifacts", "2"),
            new("recommendations", "Recommendations", "1")
        };

        object content = selectedTab.Value switch
        {
            "plan" when editing.Value => Layout.Vertical().Scroll(Scroll.Vertical).Width(Size.Full()).Height(Size.Full())
                | (Layout.Vertical().Padding(8, 6, 8, 4).Width(Size.Full().Max(Size.Units(200)))
                    | UseState(Plan).ToCodeInput().Language(Languages.Markdown).Width(Size.Full())),
            "plan" => new PlanMarkdown(Plan)
                .Article()
                .Height(Size.Full())
                .Annotations(annotations.Value)
                .CurrentAuthor("Joel")
                .OnAnnotationsChange(a => annotations.Set(a))
                .OnAnswersChange(_ => { }),
            _ => Cap(Layout.Vertical().Gap(2)
                | Text.H3(tabs.First(t => t.Id == selectedTab.Value).Label)
                | Text.Muted($"The {selectedTab.Value} tab content renders here; the workspace only frames it."))
        };

        var toolbar = Layout.Horizontal().Gap(2).Padding(2, 2, 1, 2).Height(Size.Fit())
            | new Button("Run app").Icon(Icons.Play).Outline().Tooltip("Run: npm run dev (ports: web: 5173)")
                .OnClick(() => client.Toast("Would open a terminal tab", "Review action"))
            | new Button("Open preview").Icon(Icons.Play).Outline().Disabled().Tooltip("Disabled: Condition not met (Test-Path dist)");

        var verifications = Layout.Vertical().Gap(0).Width(Size.Full())
            | buildChecked.ToBoolInput("Build")
            | lintChecked.ToBoolInput("Lint")
            | testChecked.ToBoolInput("Test").Invalid("This verification is required according to project settings")
            | (Layout.Horizontal().Gap(2).Width(Size.Full())
                | UseState(true).ToBoolInput("Screenshot", true)
                | new Badge("Pass").Variant(BadgeVariant.Success))
            | (Layout.Horizontal().Gap(2).Width(Size.Full())
                | UseState(true).ToBoolInput("CheckResults", true)
                | new Badge("Fail").Variant(BadgeVariant.Destructive));

        var questions = Layout.Vertical().Gap(2).Width(Size.Full())
            | Text.Muted("1 of 2 answered").Small()
            | (Layout.Vertical().Gap(2)
                | Text.Rich().Link("Should existing sessions be kept when the modal replaces the login page?", "sessions")
                    .OnLinkClick((string _) => selectedTab.Set("plan"))
                | Text.Rich().Link("Which feature flag service gates the rollout?", "flag", strikeThrough: true, color: Colors.Muted)
                    .OnLinkClick((string _) => selectedTab.Set("plan")));

        var chat = new ChatWidgetControl
        {
            ActiveSessionId = SessionId,
            Sessions =
            [
                new ChatSessionDto(SessionId, "#59 Revamp the User Authentication Experience", "claude", "opus",
                    "2026-09-09T09:41:00Z", "2026-09-09T09:44:00Z", Conversation, SpawnedJobs: SpawnedJobs.ToList())
            ],
            Greeting = "#59 Revamp the User Authentication Experience",
            Headline = "Ask Tendril to Change Anything",
            Embedded = true,
            Agents = [new AgentOptionDto("claude", "Claude Code", "ClaudeCode", [new ModelOptionDto("opus", "Opus")], true)],
            Models = [new ModelOptionDto("opus", "Opus")],
            Efforts = [new EffortOptionDto("default", "Default"), new EffortOptionDto("high", "High")],
            SelectedAgent = "claude",
            SelectedModel = "opus",
            OnSendMessage = e =>
            {
                client.Toast(e.Value.Prompt, "Sent to the plan chat");
                return ValueTask.CompletedTask;
            }
        }.WithLayout().Full();

        var workspace = new PlanWorkspaceControl(content, chat, verifications, questions, toolbar)
            .PlanId("#59")
            .Title("Revamp the User Authentication Experience")
            .Project("ivy-tendril", "Amber")
            .Meta("1/32 plans · Depends on #57, #58")
            .Source("https://github.com/Ivy-Interactive/Ivy-Tendril/issues/59", "Issue")
            .Persona(shareMode.Value ? "Curious Otter" : null, shareMode.Value ? "CO" : null)
            .Actions(actions)
            .MenuItems(menu)
            .Primary(primary)
            .Secondary(secondary)
            .Tabs(tabs)
            .SelectedTab(selectedTab.Value)
            .QuestionsLabel("Questions (1 unanswered)")
            .UnansweredQuestions(1)
            .OnTabSelect(id => selectedTab.Set(id))
            .OnAction(tag =>
            {
                switch (tag)
                {
                    case "Edit": editing.Set(true); break;
                    case "Save":
                    case "Cancel": editing.Set(false); break;
                    case "Execute": executing.Set(true); break;
                    case "Discuss": client.Toast("Would post the discussion opener into the chat", "Discuss"); break;
                    default: client.Toast(tag, "Action"); break;
                }
            })
            .WithLayout().Full().RemoveParentPadding();

        var toggles = new FloatingPanel(
                Layout.Horizontal().Gap(2)
                | new Button(shareMode.Value ? "Leave share mode" : "Share mode",
                    () => shareMode.Set(!shareMode.Value)).Small().Variant(ButtonVariant.Secondary))
            .Offset(new Thickness(0, 0, 8, 8));

        return new Fragment(workspace, toggles);

        static object Cap(object inner) => Layout.Vertical().Scroll().HideScrollbar().Width(Size.Full()).Height(Size.Full())
            | (Layout.Vertical().Padding(8, 6, 8, 4).Width(Size.Full().Max(Size.Units(200))) | inner);
    }

    /// <summary>
    ///     Two highlights in the opening paragraphs. Offsets count the rendered text, where every
    ///     block is followed by a newline, which is what the plain source gives for headings and
    ///     single-line paragraphs. Each highlight appends its author's initials to the text, and
    ///     the widget counts those too, so every later highlight shifts by the initials before it,
    ///     exactly as it would have when created by selecting text in the widget.
    /// </summary>
    private static ImmutableList<MarkdownAnnotation> InitialAnnotations()
    {
        var rendered = string.Join("\n", Plan.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => line.TrimStart('#', ' ')));

        var first = Annotation(rendered, "introduce a \"show/hide password\" toggle to improve user experience",
            "Is this in scope? The design does not show an eye icon.", "Joel");
        var second = Annotation(rendered, "enable it for internal accounts for one week",
            "Two weeks, to cover the on-call rotation.", "Mia", resolved: true, shift: Initials("Joel").Length);
        return [first, second];
    }

    private static MarkdownAnnotation Annotation(
        string rendered, string selectedText, string comment, string author, bool resolved = false, int shift = 0)
    {
        var start = rendered.IndexOf(selectedText, StringComparison.Ordinal) + shift;
        return new MarkdownAnnotation
        {
            StartOffset = start,
            EndOffset = start + selectedText.Length,
            SelectedText = selectedText,
            Comment = comment,
            Author = author,
            IsResolved = resolved
        };
    }

    /// <summary>Mirrors the widget's badge text: two letters of a single name, else first letters of the first two.</summary>
    private static string Initials(string name)
    {
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant() : $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
    }
}
