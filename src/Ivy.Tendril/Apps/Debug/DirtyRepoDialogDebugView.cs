using Ivy.Tendril.Apps.Views.Dialogs;
using Ivy.Tendril.Hooks;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Apps.Debug;

// Visual test harness for DirtyRepoDialog: pick a scenario to preview how the dialog renders
// that permutation of repo/dirty-state inputs.
public class DirtyRepoDialogDebugView : ViewBase
{
    private record DirtyScenario(
        string Title,
        string Description,
        PreflightResult Preflight,
        string ProceedLabel,
        string ContextMessage);

    private const string CreateContext =
        "The plan will be based on this state, but ExecutePlan will branch from origin/<baseBranch>. Commit and push first if these changes should be included in the plan.";

    private const string ExecuteContext =
        "These changes will NOT be included in this plan. The plan will execute against origin/<baseBranch>. If these changes are meant for this plan, commit and push them first.";

    private static DirtyRepoResult Repo(params DirtyReasonDetail[] reasons) =>
        new() { Reasons = reasons.ToList() };

    private static DirtyReasonDetail UncommittedChanges(params string[] files) =>
        new()
        {
            Reason = DirtyReason.UncommittedChanges,
            Message = $"{files.Length} {(files.Length == 1 ? "file" : "files")} with uncommitted changes",
            // Real GitService strips the `git status --porcelain` status prefix, so Files holds
            // clean repo-relative paths (e.g. src/Program.cs).
            Files = files.ToList()
        };

    private static DirtyReasonDetail UntrackedFiles(params string[] files) =>
        new()
        {
            Reason = DirtyReason.UntrackedFiles,
            Message = $"{files.Length} untracked {(files.Length == 1 ? "file" : "files")}",
            Files = files.ToList()
        };

    private static DirtyReasonDetail AheadOfOrigin(params string[] commits) =>
        new()
        {
            Reason = DirtyReason.AheadOfOrigin,
            Message = $"{commits.Length} {(commits.Length == 1 ? "commit" : "commits")} ahead of origin",
            Commits = commits.ToList()
        };

    private static DirtyReasonDetail NotOnExpectedBranch(string current, string expected) =>
        new()
        {
            Reason = DirtyReason.NotOnExpectedBranch,
            Message = $"On branch '{current}', expected '{expected}'"
        };

    private static DirtyReasonDetail InProgressOperation(string message) =>
        new() { Reason = DirtyReason.InProgressOperation, Message = message };

    private static DirtyReasonDetail DetachedHead() =>
        new() { Reason = DirtyReason.DetachedHead, Message = "Detached HEAD" };

    private static DirtyReasonDetail NoRemoteConfigured() =>
        new() { Reason = DirtyReason.NoRemoteConfigured, Message = "No remote configured" };

    private static PreflightResult Preflight(params (string Path, string BaseBranch, DirtyRepoResult Result)[] repos) =>
        new(repos.Select(r => (r.Path, r.BaseBranch, r.Result)).ToList());

    private static DirtyScenario[] BuildScenarios() =>
    [
        new(
            "Single repo · few uncommitted changes",
            "One repo with a handful of uncommitted files (under the 3-item cap).",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                UncommittedChanges("src/Program.cs", "README.md")))),
            "Create Without Syncing", CreateContext),

        new(
            "Single repo · many uncommitted changes (+N more)",
            "Exercises the '+N more' truncation when more than 3 files are dirty.",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                UncommittedChanges(
                    "src/Program.cs", "src/Startup.cs", "src/Models/User.cs",
                    "src/Models/Order.cs", "README.md", "global.json")))),
            "Create Without Syncing", CreateContext),

        new(
            "Single repo · untracked files",
            "Untracked files are shown verbatim (no porcelain status prefix).",
            Preflight((@"C:\Repos\MyApp", "develop", Repo(
                UntrackedFiles("notes.txt", "scratch/temp.log", "data/cache.bin")))),
            "Execute Without Syncing", ExecuteContext),

        new(
            "Single repo · ahead of origin (commits)",
            "Lists commits rather than files for the AheadOfOrigin reason.",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                AheadOfOrigin(
                    "a1b2c3d Fix null ref in parser",
                    "e4f5g6h Add retry logic",
                    "i7j8k9l Bump dependencies",
                    "m0n1o2p Tidy up logging")))),
            "Create Without Syncing", CreateContext),

        new(
            "Single repo · not on expected branch",
            "Branch-mismatch reason which carries only a message, no file/commit list.",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                NotOnExpectedBranch("feature/login", "main")))),
            "Execute Without Syncing", ExecuteContext),

        new(
            "Single repo · multiple reasons",
            "Several dirty reasons stacked in a single repo section.",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                NotOnExpectedBranch("feature/login", "main"),
                UncommittedChanges("src/Auth.cs", "src/Login.cs"),
                UntrackedFiles("debug.log"),
                AheadOfOrigin("a1b2c3d WIP commit")))),
            "Create Without Syncing", CreateContext),

        new(
            "Single repo · edge-case reasons",
            "Detached HEAD, in-progress operation, and no remote configured.",
            Preflight((@"C:\Repos\MyApp", "main", Repo(
                DetachedHead(),
                InProgressOperation("Rebase in progress"),
                NoRemoteConfigured()))),
            "Execute Without Syncing", ExecuteContext),

        new(
            "Multiple repos",
            "Two repos each with their own reasons — tests repeated repo sections.",
            Preflight(
                (@"C:\Repos\Frontend", "main", Repo(
                    UncommittedChanges("src/app.tsx", "package.json"))),
                (@"C:\Repos\Backend", "develop", Repo(
                    AheadOfOrigin("a1b2c3d Add endpoint"),
                    UntrackedFiles("appsettings.Local.json")))),
            "Create Without Syncing", CreateContext),

        new(
            "Many repos · long paths",
            "Several repos with deep paths to check layout/width handling.",
            Preflight(
                (@"C:\Users\dev\source\repos\Company.Product.Web", "main", Repo(
                    UncommittedChanges("src/Components/Dashboard/Widgets/ChartWidget.razor"))),
                (@"C:\Users\dev\source\repos\Company.Product.Api", "main", Repo(
                    UncommittedChanges("Controllers/v2/ReportingController.cs"))),
                (@"C:\Users\dev\source\repos\Company.Product.Shared", "release/2026.06", Repo(
                    NotOnExpectedBranch("hotfix/urgent", "release/2026.06")))),
            "Execute Without Syncing", ExecuteContext),
    ];

    public override object Build()
    {
        var dialogOpen = UseState(false);
        var selected = UseState<int?>(() => null);
        var scenarios = BuildScenarios();

        var buttons = Layout.Vertical();
        for (var i = 0; i < scenarios.Length; i++)
        {
            var index = i;
            var scenario = scenarios[i];
            buttons |= Layout.Vertical().Gap(1)
                       | new Button(scenario.Title)
                           .Outline()
                           .Icon(Icons.Eye)
                           .OnClick(() =>
                           {
                               selected.Set(index);
                               dialogOpen.Set(true);
                           })
                       | Text.Muted(scenario.Description);
        }

        object? dialog = null;
        if (dialogOpen.Value && selected.Value is { } selectedIndex)
        {
            var scenario = scenarios[selectedIndex];
            dialog = new DirtyRepoDialog(
                dialogOpen,
                scenario.Preflight,
                scenario.ProceedLabel,
                scenario.ContextMessage,
                onSyncRepos: () => { },
                onProceed: () => { });
        }

        return new Fragment(buttons, dialog);
    }
}
