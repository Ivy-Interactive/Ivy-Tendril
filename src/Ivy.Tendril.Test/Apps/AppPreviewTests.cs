using System.Collections.Immutable;
using Ivy.Tendril;
using Ivy.Tendril.Apps.ReviewAction;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Apps;

// A review action becomes a WebViewer the moment the app it started says where it is serving,
// so what counts as "where it is serving" is the whole hinge of the feature: miss it and the
// reviewer is left in a terminal, get it wrong and they are looking at a documentation page.
public class AppPreviewTests
{
    [Theory]
    [InlineData("Ivy is running on https://localhost:5011 [30668]", "https://localhost:5011")]
    [InlineData("  ➜  Local:   http://localhost:5173/", "http://localhost:5173/")]
    [InlineData("Now listening on: http://127.0.0.1:5000", "http://127.0.0.1:5000")]
    [InlineData("started at http://[::1]:8080/", "http://[::1]:8080/")]
    public void DetectUrl_FindsWhatADevServerPrints(string transcript, string expected)
    {
        Assert.Equal(expected, AppPreview.DetectUrl(transcript));
    }

    // Terminal output wraps URLs in prose; the punctuation around one is not part of it.
    [Theory]
    [InlineData("serving on http://localhost:3000.", "http://localhost:3000")]
    [InlineData("open the app (http://localhost:3000)", "http://localhost:3000")]
    [InlineData("see <http://localhost:3000>", "http://localhost:3000")]
    public void DetectUrl_TrimsTrailingPunctuation(string transcript, string expected)
    {
        Assert.Equal(expected, AppPreview.DetectUrl(transcript));
    }

    // A build prints plenty of URLs that are not the app. None of them name a port.
    [Theory]
    [InlineData("")]
    [InlineData("error NU1101: see https://aka.ms/nuget-error for details")]
    [InlineData("Restored from https://api.nuget.org/v3/index.json")]
    [InlineData("For help, visit https://vitejs.dev/guide/")]
    public void DetectUrl_IgnoresUrlsThatCannotBeADevServer(string transcript)
    {
        Assert.Null(AppPreview.DetectUrl(transcript));
    }

    [Fact]
    public void DetectUrl_PrefersLoopbackOverTheNetworkAddress()
    {
        const string transcript = """
              ➜  Network: http://192.168.1.9:5173/
              ➜  Local:   http://localhost:5173/
            """;

        Assert.Equal("http://localhost:5173/", AppPreview.DetectUrl(transcript));
    }

    // Bound to 0.0.0.0, a dev server prints only its LAN address — still the app.
    [Fact]
    public void DetectUrl_TakesANetworkAddressWhenThereIsNoLoopbackOne()
    {
        Assert.Equal("http://192.168.1.9:5173/", AppPreview.DetectUrl("  ➜  Network: http://192.168.1.9:5173/"));
    }

    [Theory]
    [InlineData("http://localhost:5173/", true)]
    [InlineData("http://127.0.0.1:5000/", true)]
    [InlineData("http://[::1]:8080/", true)]
    [InlineData("http://192.168.1.9:5173/", true)]
    [InlineData("http://10.1.2.3:3000/", true)]
    [InlineData("http://172.16.0.4:3000/", true)]
    [InlineData("http://172.31.255.1:3000/", true)]
    [InlineData("http://169.254.1.1/", true)]
    [InlineData("http://172.32.0.1:3000/", false)]
    [InlineData("http://8.8.8.8:3000/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("https://ivy.app/", false)]
    public void IsLocalTarget_AdmitsThisMachineAndItsNetworkAndNothingElse(string url, bool expected)
    {
        Assert.Equal(expected, AppPreview.IsLocalTarget(new Uri(url)));
    }

    [Theory]
    // The app under review, exactly as before.
    [InlineData("http://localhost:5173/", true)]
    [InlineData("http://192.168.1.9:5173/", true)]
    // The static assets that app links, or it renders in fallback fonts with no icons.
    [InlineData("https://fonts.googleapis.com/css2?family=IBM+Plex+Mono", true)]
    [InlineData("https://fonts.gstatic.com/s/ibmplexmono/v19/x.woff2", true)]
    [InlineData("https://cdn.jsdelivr.net/npm/chart.js", true)]
    [InlineData("https://unpkg.com/react@18/umd/react.production.min.js", true)]
    // Host match is exact: a lookalike registered by someone else is not on the list.
    [InlineData("https://fonts.googleapis.com.evil.test/x.css", false)]
    [InlineData("https://evil.test/fonts.googleapis.com", false)]
    // HTTPS only. None of these need plaintext, and insisting costs nothing.
    [InlineData("http://fonts.googleapis.com/css2", false)]
    // Everything else is still refused: this is not a general-purpose browser.
    [InlineData("https://example.com/", false)]
    [InlineData("http://8.8.8.8:3000/", false)]
    public void IsAllowedTarget_AdmitsTheAppAndTheAssetsItLinksAndNothingElse(string url, bool expected)
    {
        Assert.Equal(expected, AppPreview.IsAllowedTarget(new Uri(url)));
    }

    [Fact]
    public void SourceLabel_ReadsTheResolvedFileAndLine()
    {
        const string debug = """{"source":{"file":"src/App.tsx","line":59,"col":12},"confidence":"high"}""";

        Assert.Equal("src/App.tsx:59", AppPreview.SourceLabel(debug));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"source":null}""")]
    [InlineData("""{"frames":[{"url":"https://x/a.js","line":1,"col":2}]}""")]
    public void SourceLabel_IsNullWhenNothingResolved(string? debugJson)
    {
        Assert.Null(AppPreview.SourceLabel(debugJson));
    }

    [Fact]
    public void FormatChangeRequest_LeadsWithTheSourceLocationWhenThereIsOne()
    {
        var comments = ImmutableList.Create(
            new AppComment("c1", 1, "button", "main > button", "Make this green",
                """{"source":{"file":"src/App.tsx","line":59}}"""),
            new AppComment("c2", 2, "div", "main > div.hero", "Too much padding", null));

        var request = AppPreview.FormatChangeRequest("http://localhost:5173/", comments);

        Assert.Contains("http://localhost:5173/", request);
        Assert.Contains("**1. <button>** in `src/App.tsx:59`", request);
        Assert.Contains("Make this green", request);
        // Nothing resolved for the second one, so the selector is all the agent gets.
        Assert.Contains("**2. <div>**", request);
        Assert.DoesNotContain("**2. <div>** in", request);
        Assert.Contains("main > div.hero", request);
    }

    [Fact]
    public void FormatChangeRequest_GroupsCommentsUnderThePageTheyWereLeftOn()
    {
        var comments = ImmutableList.Create(
            new AppComment("c1", 1, "input", "main input", "Placeholder is too vague", null,
                "http://localhost:5174/tool/html-encoder"),
            new AppComment("c2", 2, "a", "nav a.home", "Wrong hover colour", null,
                "http://localhost:5174/"),
            new AppComment("c3", 3, "button", "main button", "Encode should be primary", null,
                "http://localhost:5174/tool/html-encoder"));

        var request = AppPreview.FormatChangeRequest("http://localhost:5174/", comments);

        Assert.Contains("## http://localhost:5174/tool/html-encoder", request);
        Assert.Contains("## http://localhost:5174/", request);

        // Two pages, and the one that was commented on first leads. Both of that page's
        // comments sit under its heading rather than being split across the list.
        var encoder = request.IndexOf("## http://localhost:5174/tool/html-encoder", StringComparison.Ordinal);
        var home = request.IndexOf("## http://localhost:5174/" + Environment.NewLine, StringComparison.Ordinal);
        Assert.True(encoder < home, "pages should keep the order their first comment arrived in");
        Assert.True(request.IndexOf("Encode should be primary", StringComparison.Ordinal) < home);
        Assert.True(request.IndexOf("Wrong hover colour", StringComparison.Ordinal) > home);
    }

    private static JobItem Job(string id, string type, JobStatus status) =>
        new() { Id = id, Type = type, Status = status };

    [Fact]
    public void JobsToWaitFor_ChainsBehindEverythingStillInFlight()
    {
        var jobs = new[]
        {
            Job("1", Constants.JobTypes.RetryPlan, JobStatus.Running),
            // Already queued behind #1. The next request goes after it, not beside it — that is
            // what makes a third Update a chain rather than three agents on one worktree.
            Job("2", Constants.JobTypes.RetryPlan, JobStatus.Blocked),
            Job("3", Constants.JobTypes.CreatePr, JobStatus.Queued),
            Job("4", Constants.JobTypes.ExecutePlan, JobStatus.Pending),
        };

        Assert.Equal(new[] { "1", "2", "3", "4" }, AppPreview.JobsToWaitFor(jobs));
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Timeout)]
    [InlineData(JobStatus.Stopped)]
    public void JobsToWaitFor_IgnoresJobsThatAreOver(JobStatus status)
    {
        var jobs = new[] { Job("1", Constants.JobTypes.RetryPlan, status) };

        Assert.Empty(AppPreview.JobsToWaitFor(jobs));
    }

    [Fact]
    public void CanRequestChanges_WhileThePlanIsInReview()
    {
        Assert.True(AppPreview.CanRequestChanges(PlanStatus.Review, []));
    }

    [Fact]
    public void CanRequestChanges_WhileARetryIsAlreadyRunning()
    {
        // A retry moves the plan to Executing. A reviewer who keeps walking the app and finds
        // three more things should be able to queue them instead of being locked out until the
        // agent happens to finish.
        var jobs = new[] { Job("1", Constants.JobTypes.RetryPlan, JobStatus.Running) };

        Assert.True(AppPreview.CanRequestChanges(PlanStatus.Executing, jobs));
    }

    [Fact]
    public void CanRequestChanges_WhileARetryIsQueuedBehindAnother()
    {
        var jobs = new[] { Job("1", Constants.JobTypes.RetryPlan, JobStatus.Blocked) };

        Assert.True(AppPreview.CanRequestChanges(PlanStatus.Executing, jobs));
    }

    [Theory]
    [InlineData(PlanStatus.Draft)]
    [InlineData(PlanStatus.Creating)]
    [InlineData(PlanStatus.Executing)]
    [InlineData(PlanStatus.Completed)]
    [InlineData(PlanStatus.Failed)]
    [InlineData(PlanStatus.Skipped)]
    public void CanRequestChanges_NotWhenThePlanIsElsewhereWithNoRetryRunning(PlanStatus state)
    {
        // An ExecutePlan running is not a retry: the plan is being built for the first time, and
        // feedback on a running app has nowhere to go yet.
        var jobs = new[] { Job("1", Constants.JobTypes.ExecutePlan, JobStatus.Running) };

        Assert.False(AppPreview.CanRequestChanges(state, jobs));
    }

    [Fact]
    public void CanRequestChanges_NotOnARetryThatHasAlreadyFinished()
    {
        var jobs = new[] { Job("1", Constants.JobTypes.RetryPlan, JobStatus.Completed) };

        Assert.False(AppPreview.CanRequestChanges(PlanStatus.Executing, jobs));
    }

    [Fact]
    public void FormatChangeRequest_FallsBackToTheAppUrlForACommentWithNoPage()
    {
        var comments = ImmutableList.Create(
            new AppComment("c1", 1, "button", "main button", "Make this green", null));

        var request = AppPreview.FormatChangeRequest("http://localhost:5173/", comments);

        Assert.Contains("## http://localhost:5173/", request);
    }
}
