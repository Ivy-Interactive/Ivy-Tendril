using System.Text.RegularExpressions;
using Ivy.Tendril.Test.TestHelpers;

namespace Ivy.Tendril.Test;

/// <summary>
///     <c>IPlanWatcherService.PlansChanged</c> fires once per changed plan and several times per plan
///     mutation. A subscriber that neither coalesces nor (for a view showing one plan) drops events naming
///     another plan is how #2571 happened, and how it comes back. Every subscriber in the product has been
///     audited against both rules; this test fails when one is added that has not been.
/// </summary>
public class PlansChangedSubscriberAuditTests
{
    /// <summary>
    ///     Every file in <c>src/Ivy.Tendril</c> allowed to subscribe to <c>PlansChanged</c>, with how each
    ///     one satisfies the two rules. Paths are relative to the repo root, with forward slashes.
    /// </summary>
    private static readonly Dictionary<string, string> AuditedSubscribers = new()
    {
        ["src/Ivy.Tendril/Apps/Review/ContentView.cs"] =
            "Gates through PlanRefreshGate, coalesces through RefreshCoalescer (400ms).",
        ["src/Ivy.Tendril/Apps/Plans/ContentView.cs"] =
            "Gates through PlanRefreshGate, coalesces through RefreshCoalescer (PlanRefreshWindow).",
        ["src/Ivy.Tendril/Hooks/UseInboxAutoRefresh.cs"] =
            "Folder-blind by design (list views: membership and ordering change for plans not listed), "
            + "coalesces through RefreshCoalescer (400ms).",
        ["src/Ivy.Tendril/Services/Plans/PlanDatabaseSyncService.cs"] =
            "Syncs all plans, so no folder gate applies; queues off the raising thread and reruns once "
            + "after the pass in flight.",
        ["src/Ivy.Tendril/Services/Plans/TendrilProcessStatusService.cs"] =
            "Counts span all plans, so no folder gate applies; 200ms debounce plus record-equality dedupe.",
    };

    /// <summary>Detail views: showing one plan, so the folder gate is not optional for them.</summary>
    private static readonly string[] SinglePlanViews =
    [
        "src/Ivy.Tendril/Apps/Review/ContentView.cs",
        "src/Ivy.Tendril/Apps/Plans/ContentView.cs",
    ];

    [Fact]
    public void EveryPlansChangedSubscriber_IsAudited()
    {
        var repoRoot = RepoRoot.Find();
        var found = FindSubscriberFiles(repoRoot);

        var unaudited = found.Except(AuditedSubscribers.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(unaudited.Count == 0,
            $"New PlansChanged subscriber(s) not in the audit: {string.Join(", ", unaudited)}.\n"
            + "PlansChanged fires once per changed plan and several times per plan mutation, so:\n"
            + "  1. Coalesce - feed a RefreshCoalescer (or an equivalent debounce), never work inline.\n"
            + "  2. Gate, if the subscriber renders a single plan - drop events naming another plan via\n"
            + "     PlanRefreshGate.ShouldRefreshFor. A subscriber whose work spans all plans (a list, a\n"
            + "     count, a full sync) is folder-blind by design and must not gate.\n"
            + "Then add the file to AuditedSubscribers in this test with which of the two applies and why.");
    }

    [Fact]
    public void AuditedSubscribers_AllStillSubscribe()
    {
        // The other direction: an entry left behind after its subscription is removed makes the audit
        // read as broader than it is.
        var repoRoot = RepoRoot.Find();
        var found = FindSubscriberFiles(repoRoot);

        var stale = AuditedSubscribers.Keys.Except(found, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.True(stale.Count == 0,
            $"Audited file(s) no longer subscribe to PlansChanged: {string.Join(", ", stale)}. "
            + "Remove them from AuditedSubscribers in this test.");
    }

    [Fact]
    public void SinglePlanViews_GateThroughPlanRefreshGate()
    {
        // The census above cannot see whether a view still gates, only that it subscribes - so assert the
        // gate itself for the two views that render one plan.
        var repoRoot = RepoRoot.Find();

        foreach (var relative in SinglePlanViews)
        {
            var source = File.ReadAllText(Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains("PlanRefreshGate.ShouldRefreshFor", source);
            Assert.Contains("RefreshCoalescer", source);
        }
    }

    [Fact]
    public void PlansContentView_QueryRevalidatesOnRefresh()
    {
        // The gate and the coalescer are worth nothing if nothing revalidates at the end of them: the
        // Plans content query shipped subscribed to nothing at all, at the default server scope, so the
        // open plan's content stayed as it was first read. Pinned as source text because the repo has no
        // way to render a view in a test - the coalescing behaviour itself is covered at the hook seam in
        // PlansContentViewRefreshTests.
        var repoRoot = RepoRoot.Find();
        var source = File.ReadAllText(
            Path.Combine(repoRoot, "src", "Ivy.Tendril", "Apps", "Plans", "ContentView.cs"));

        Assert.Contains("QueryScope.View", source);
        Assert.Contains("Revalidate()", source);
    }

    private static List<string> FindSubscriberFiles(string repoRoot)
    {
        var productDir = Path.Combine(repoRoot, "src", "Ivy.Tendril");
        var subscription = new Regex(@"PlansChanged\s*\+=", RegexOptions.Compiled);

        return Directory.EnumerateFiles(productDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(f => subscription.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
