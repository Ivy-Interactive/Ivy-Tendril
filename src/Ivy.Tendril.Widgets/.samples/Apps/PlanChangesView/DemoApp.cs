using Ivy;
using Ivy.Tendril.Widgets;
using PlanChangesViewControl = Ivy.Tendril.Widgets.PlanChangesView;

namespace WidgetSamples.Apps.PlanChangesView;

[App(title: "Changes", icon: Icons.GitCompare, group: ["PlanChangesView"])]
class DemoApp : ViewBase
{
    private static readonly List<ChangedFileDto> Files =
    [
        Modified("src/Ivy.Tendril.Test/Apps/Chat/DeleteSessionDialogTests.cs", 1, 1, """
            @@ -25,7 +25,7 @@ public class FakeSessions
                     public IReadOnlyList<ChatSessionModel> GetSessions() => Sessions;
                     public ChatSessionModel? GetSession(string id) => Sessions.Find(s => s.Id == id);
            -        public ChatSessionModel CreateSession(string agentId, string modelId, string? title = null, string? effort = null)
            +        public ChatSessionModel CreateSession(string agentId, string modelId, string? title = null, string? effort = null, string? planFolderName = null)
                     {
                         var session = new ChatSessionModel(Guid.NewGuid().ToString(), title ?? "New Chat", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, agentId, modelId, [], effort);
                         Sessions.Add(session);
            """),
        Modified("src/Ivy.Tendril.Test/FakePlanReaderService.cs", 7, 0, """
            @@ -10,4 +10,11 @@ public class FakePlanReaderService : IPlanReaderService
             {
                 public List<PlanFile> Plans { get; } = [];
            +    public PlanFile? GetPlanByFolder(string folderName)
            +    {
            +        return Plans.Find(p => p.FolderName == folderName);
            +    }
            +
            +    public IReadOnlyList<PlanFile> GetPlans() => Plans;
            +
                 public string? GetCompletionBlockReason(string folderName) => null;
             }
            """),
        Added("src/Ivy.Tendril.Test/PlanChatTests.cs", 12, """
            @@ -0,0 +1,12 @@
            +using Xunit;
            +
            +namespace Ivy.Tendril.Test;
            +
            +public class PlanChatTests
            +{
            +    [Fact]
            +    public void AttachesPlanToSession()
            +    {
            +        Assert.True(true);
            +    }
            +}
            """),
        Added("src/Ivy.Tendril.Widgets/.samples/Apps/PlanWorkspace/DemoApp.cs", 5, """
            @@ -0,0 +1,5 @@
            +using Ivy;
            +
            +namespace WidgetSamples.Apps.PlanWorkspace;
            +
            +class DemoApp : ViewBase { public override object Build() => Text.Muted("demo"); }
            """),
        Modified("src/Ivy.Tendril.Widgets/AGENTS.md", 3, 0, """
            @@ -20,4 +20,7 @@ frontend/             React/Vite bundle (npm run build → dist/)
                 ChatWidget/       DemoApp
                 PlanWorkspace/    DemoApp
            +    PlanChangesView/  DemoApp (file tree beside the diff list, draft comments)
            +
            +Widgets register on window.IvyTendrilWidgets.
                 TendrilQuestions/ DemoApp
                 WebViewer/        DemoApp (inspector), SideBySideApp (two viewers on one page)
            """),
        Modified("src/Ivy.Tendril.Widgets/frontend/src/ChatWidget/AgentPicker.tsx", 5, 1, """
            @@ -12,6 +12,10 @@ export function AgentPicker({ agents, onSelect }: AgentPickerProps) {
               return (
                 <div className="cw-agent-picker">
            -      {agents.map((a) => <button key={a.id} onClick={() => onSelect(a.id)}>{a.label}</button>)}
            +      {agents.map((a) => (
            +        <button key={a.id} type="button" onClick={() => onSelect(a.id)}>
            +          {a.label}
            +        </button>
            +      ))}
                 </div>
               );
             }
            """),
        Deleted("src/Ivy.Tendril/Apps/Views/LegacyTreePanel.cs", 4, """
            @@ -1,4 +0,0 @@
            -namespace Ivy.Tendril.Apps.Views;
            -
            -public class LegacyTreePanel : ViewBase
            -{ public override object Build() => Text.Muted("legacy"); }
            """),
    ];

    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var showTree = UseState(true);
        var hideFormatting = UseState(true);
        var comments = UseState(() => new List<DraftComment>
        {
            new("src/Ivy.Tendril.Test/PlanChatTests.cs", "I8", "Give this test a real assertion.", 8, "Reviewer"),
        });

        var view = new PlanChangesViewControl
        {
            Key = "demo",
            Files = Files,
            Comments = comments.Value,
            ShowTree = showTree.Value,
            OnAddComment = e =>
            {
                comments.Set([.. comments.Value, e.Value]);
                client.Toast($"{e.Value.FilePath}:{e.Value.LineNumber}", "OnAddComment").Info();
                return ValueTask.CompletedTask;
            },
            OnUpdateComment = e =>
            {
                var list = new List<DraftComment>(comments.Value);
                var idx = list.FindIndex(c => c.FilePath == e.Value.FilePath && c.ChangeKey == e.Value.ChangeKey);
                if (idx >= 0) list[idx] = e.Value;
                comments.Set(list);
                client.Toast($"{e.Value.FilePath}:{e.Value.LineNumber}", "OnUpdateComment").Info();
                return ValueTask.CompletedTask;
            },
            OnDeleteComment = e =>
            {
                comments.Set(comments.Value.Where(c => c.FilePath != e.Value.FilePath || c.ChangeKey != e.Value.ChangeKey).ToList());
                client.Toast($"{e.Value.FilePath}:{e.Value.LineNumber}", "OnDeleteComment").Info();
                return ValueTask.CompletedTask;
            },
        }.Width(Size.Full()).Height(Size.Full());

        var toolbar = Layout.Horizontal().Gap(1).Height(Size.Auto())
            | new TendrilIconButton(showTree.Value ? "Hide file tree" : "Show file tree", "ListTree")
                .Size(TendrilIconButtonSize.Md)
                .Active(showTree.Value)
                .OnClick(() => showTree.Set(!showTree.Value))
            | new TendrilIconButton(hideFormatting.Value ? "Show formatting changes" : "Hide formatting changes", "EyeOff")
                .Size(TendrilIconButtonSize.Md)
                .Active(hideFormatting.Value)
                .OnClick(() => hideFormatting.Set(!hideFormatting.Value));

        return Layout.Vertical().Gap(2).Height(Size.Full().Min(Size.Px(0))).Padding(4)
            | toolbar
            | view;
    }

    private static ChangedFileDto Modified(string path, int additions, int deletions, string hunk) =>
        new(path, "M", $"diff --git a/{path} b/{path}\n--- a/{path}\n+++ b/{path}\n{hunk}\n", additions, deletions);

    private static ChangedFileDto Added(string path, int additions, string hunk) =>
        new(path, "A", $"diff --git a/{path} b/{path}\nnew file mode 100644\n--- /dev/null\n+++ b/{path}\n{hunk}\n", additions, 0);

    private static ChangedFileDto Deleted(string path, int deletions, string hunk) =>
        new(path, "D", $"diff --git a/{path} b/{path}\ndeleted file mode 100644\n--- a/{path}\n+++ /dev/null\n{hunk}\n", 0, deletions);
}
