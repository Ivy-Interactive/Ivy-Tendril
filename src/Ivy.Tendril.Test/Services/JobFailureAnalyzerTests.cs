using Ivy.Tendril.Services.Jobs;

namespace Ivy.Tendril.Test.Services;

public class JobFailureAnalyzerTests
{
    // Regression for #1263: a `gh api` JSON response containing an `"error": { ... }` object
    // must NOT be mislabeled as a Claude API / usage error. The real (gh) message should surface.
    [Fact]
    public void GhApiErrorObject_NotLabeledAsClaudeApi()
    {
        var output = new List<string>
        {
            "[stderr] {\"message\":\"Not Found\",\"error\":{\"code\":\"missing_field\"},\"documentation_url\":\"https://docs.github.com\"}",
        };

        var reason = JobFailureAnalyzer.ExtractFailureReason(output, "CreatePr");

        Assert.DoesNotContain("Claude API", reason);
        Assert.Contains("Not Found", reason);
    }

    // A `"type":"error"` stream event that carries no Claude API error token should be reported
    // as its plain message — not framed as a usage/quota problem.
    [Fact]
    public void ClaudeStreamErrorWithoutToken_NotFramedAsUsage()
    {
        var output = new List<string>
        {
            "{\"type\":\"error\",\"error\":{\"message\":\"something went wrong internally\"}}",
        };

        var reason = JobFailureAnalyzer.ExtractFailureReason(output, "CreatePr");

        Assert.DoesNotContain("Claude API", reason);
        Assert.Contains("something went wrong internally", reason);
    }

    // A genuine rate-limit error must still be classified as a Claude API error.
    [Fact]
    public void RateLimitError_StillClassifiedAsClaudeApi()
    {
        var output = new List<string>
        {
            "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Number of requests exceeded\"}}",
        };

        var reason = JobFailureAnalyzer.ExtractFailureReason(output, "CreatePr");

        Assert.StartsWith("Claude API:", reason);
        Assert.Contains("Number of requests exceeded", reason);
    }

    // A transient git failure (the actual cause behind the intermittent Create PR bug) should be
    // surfaced verbatim as the actionable reason, never as a Claude usage problem.
    [Fact]
    public void TransientGitStderr_SurfacedAsActionableReason()
    {
        var output = new List<string>
        {
            "[stderr] fatal: unable to access 'https://github.com/acme/repo.git/': Could not resolve host: github.com",
        };

        var reason = JobFailureAnalyzer.ExtractFailureReason(output, "CreatePr");

        Assert.DoesNotContain("Claude API", reason);
        Assert.Contains("Could not resolve host", reason);
    }

    [Fact]
    public void SkillsBudgetWarningEvent_DoesNotMaskStderrCause()
    {
        var output = new List<string>
        {
            """{"kind":"error","timestamp":"2026-09-07T12:00:00Z","message":"Skill descriptions were shortened to fit the 2% skills context budget.","is_retryable":false,"is_auth_error":false}""",
            "[stdout] Connecting to GitHub...",
            "[stdout] Fetching repository information...",
            "[stdout] Cloning repository...",
            "[stderr] fatal: unable to access 'https://github.com/acme/repo.git/': Could not resolve host: github.com",
        };

        var reason = JobFailureAnalyzer.ExtractFailureReason(output, "CreatePr");

        Assert.Contains("fatal:", reason);
        Assert.DoesNotContain("skill", reason, StringComparison.OrdinalIgnoreCase);
    }
}
