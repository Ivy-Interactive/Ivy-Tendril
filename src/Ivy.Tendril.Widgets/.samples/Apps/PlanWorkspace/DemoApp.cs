using Ivy;
using Ivy.Tendril.Widgets;
using ChatWidgetControl = Ivy.Tendril.Widgets.ChatWidget;
using PlanWorkspaceControl = Ivy.Tendril.Widgets.PlanWorkspace;

namespace WidgetSamples.Apps.PlanWorkspace;

/// <summary>The plan page frame: top bar actions, tabs with the Verifications/Questions dropdowns, and the always-present resizable chat panel.</summary>
[App(title: "Plan Workspace", icon: Icons.PanelRight, group: ["PlanWorkspace"])]
class DemoApp : ViewBase
{
    private const string Plan = """
        # Summary

        ## Changes

        Refactor the authentication flow to use a unified modal interface instead of full-page redirects. Add real-time inline validation for email and password fields, and introduce a "show/hide password" toggle to improve user experience. Disable submit buttons during API calls to prevent duplicate submissions.

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
        ```
        """;

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var selectedTab = UseState("plan");
        var editing = UseState(false);
        var lastAction = UseState("");

        var actions = new List<PlanActionDto>
        {
            new("Edit", "Edit", nameof(Icons.Pencil), "E"),
            new("Chat", "Update", nameof(Icons.WandSparkles), "U", FocusChat: true),
            new("Expand", "Expand", nameof(Icons.Expand), "P")
        };
        var menu = new List<PlanActionDto>
        {
            new("Split", "Split", nameof(Icons.Scissors)),
            new("Delete", "Delete", nameof(Icons.Trash), "Backspace", Danger: true),
            new("OpenInTerminal", "Open in Terminal", nameof(Icons.Terminal))
        };

        object content = selectedTab.Value switch
        {
            "details" => Layout.Vertical().Padding(8, 6, 8, 4) | Text.H3("Details") | Text.Muted("Jobs, revisions and costs would live here."),
            _ => new PlanMarkdown(Plan).Article().Height(Size.Full())
        };

        var verifications = Layout.Vertical().Gap(0)
            | UseState(true).ToBoolInput("Screenshot")
            | UseState(true).ToBoolInput("Lint")
            | UseState(true).ToBoolInput("Build")
            | UseState(false).ToBoolInput("Test");

        var questions = Layout.Vertical().Gap(2)
            | Text.Muted("0 of 1 answered").Small()
            | Text.Rich().Link("Should existing sessions be kept when the modal replaces the login page?", "sessions");

        var chat = new ChatWidgetControl
        {
            Greeting = "#59 Revamp the User Authentication Experience",
            Headline = "Ask Tendril to Change Anything",
            Embedded = true,
            Agents = [new AgentOptionDto("claude", "Claude Code", "ClaudeCode", [new ModelOptionDto("opus", "Opus")])],
            SelectedAgent = "claude",
            SelectedModel = "opus",
            OnSendMessage = e =>
            {
                lastAction.Set($"Sent: {e.Value.Prompt}");
                return ValueTask.CompletedTask;
            }
        }.WithLayout().Full().RemoveParentPadding();

        var workspace = new PlanWorkspaceControl(content, chat, verifications, questions)
            .PlanId("#59")
            .Title("Revamp the User Authentication Experience")
            .Meta("1/32 plans")
            .Actions(editing.Value ? [] : actions)
            .MenuItems(editing.Value ? [] : menu)
            .Primary(editing.Value
                ? new PlanActionDto("Save", "Save Revision", nameof(Icons.Save), "S")
                : new PlanActionDto("Execute", "Execute", nameof(Icons.Rocket), "x"))
            .Secondary(editing.Value ? [new PlanActionDto("Cancel", "Cancel", null, "Escape")] : [])
            .Tabs([new PlanTabDto("plan", "Plan"), new PlanTabDto("details", "Details"), new PlanTabDto("git", "Git", "3")])
            .SelectedTab(selectedTab.Value)
            .QuestionsLabel("Questions (1 unanswered)")
            .OnTabSelect(id => selectedTab.Set(id))
            .OnAction(tag =>
            {
                lastAction.Set(tag);
                switch (tag)
                {
                    case "Chat": break;
                    case "Edit": editing.Set(true); break;
                    case "Save":
                    case "Cancel": editing.Set(false); break;
                    default: client.Toast(tag, "Action"); break;
                }
            })
            .WithLayout().Full().RemoveParentPadding();

        return workspace;
    }
}
