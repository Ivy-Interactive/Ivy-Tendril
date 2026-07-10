using Ivy.Tendril.Apps.Drafts;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;
using ReviewContentView = Ivy.Tendril.Apps.Review.ContentView;

namespace Ivy.Tendril.Test;

public class ContentViewTests
{
    private static PlanFile CreateFailedPlan(string folderPath)
    {
        var metadata = new PlanMetadata(
            1, "Test", "Bug", "Test Plan", PlanStatus.Failed,
            [], [], [], [], [], [], DateTime.UtcNow, DateTime.UtcNow, null, null);
        return new PlanFile(metadata, "", folderPath, "");
    }

    /// <summary>
    /// Lays out a TendrilHome with plan 00001 and, optionally, one Job Log for it in Jobs/.
    /// Returns (tendrilHome, planFolder).
    /// </summary>
    private static (string TendrilHome, string PlanFolder) CreateHome(string? jobLogContent)
    {
        var tendrilHome = Path.Combine(Path.GetTempPath(), $"ivy-test-{Guid.NewGuid()}");
        var planDir = Path.Combine(tendrilHome, "Plans", "00001-TestPlan");
        Directory.CreateDirectory(planDir);

        if (jobLogContent != null)
        {
            var jobsDir = Path.Combine(tendrilHome, "Jobs");
            Directory.CreateDirectory(jobsDir);
            File.WriteAllText(Path.Combine(jobsDir, "00007-00001-ExecutePlan.md"), jobLogContent);
        }

        return (tendrilHome, planDir);
    }

    [Fact]
    public void BuildFailureCallout_WithCompletedStatusLog_ShowsStateMismatch()
    {
        var (tendrilHome, planDir) = CreateHome(
            "# Job Log 00007-00001-ExecutePlan\n\n- **Status:** Completed\n- **Started:** 2026-03-30 10:00:00Z\n- **Completed:** 2026-03-30 10:05:00Z\n- **Duration:** 300s\n");
        try
        {
            var result = ContentView.BuildFailureCallout(CreateFailedPlan(planDir), tendrilHome);

            var callout = Assert.IsType<Callout>(result);
            Assert.Equal(CalloutVariant.Warning, callout.Variant);
            Assert.Equal("State Mismatch", callout.Title);
        }
        finally
        {
            if (Directory.Exists(tendrilHome))
                Directory.Delete(tendrilHome, true);
        }
    }

    [Fact]
    public void BuildFailureCallout_WithFailedStatusLog_ShowsDestructiveCallout()
    {
        var (tendrilHome, planDir) = CreateHome(
            "# Job Log 00007-00001-ExecutePlan\n\n- **Status:** Failed\n- **Started:** 2026-03-30 10:00:00Z\n");
        try
        {
            var result = ContentView.BuildFailureCallout(CreateFailedPlan(planDir), tendrilHome);

            var callout = Assert.IsType<Callout>(result);
            Assert.Equal(CalloutVariant.Destructive, callout.Variant);
            Assert.Equal("Execution Failed", callout.Title);
        }
        finally
        {
            if (Directory.Exists(tendrilHome))
                Directory.Delete(tendrilHome, true);
        }
    }

    [Fact]
    public void BuildFailureCallout_WithNoLogs_ShowsNoDetailsAvailable()
    {
        var (tendrilHome, planDir) = CreateHome(null);
        try
        {
            var result = ContentView.BuildFailureCallout(CreateFailedPlan(planDir), tendrilHome);

            var callout = Assert.IsType<Callout>(result);
            Assert.Equal(CalloutVariant.Destructive, callout.Variant);
        }
        finally
        {
            if (Directory.Exists(tendrilHome))
                Directory.Delete(tendrilHome, true);
        }
    }

    [Fact]
    public void BuildFailureCallout_IgnoresPromptFileWhenLocatingTheJobLog()
    {
        var (tendrilHome, planDir) = CreateHome(
            "# Job Log 00007-00001-ExecutePlan\n\n- **Status:** Failed\n");
        try
        {
            // The Job Prompt shares the plan-id segment and also ends in .md — it must not be picked up.
            File.WriteAllText(
                Path.Combine(tendrilHome, "Jobs", "00007-00001-ExecutePlan.prompt.md"),
                "## Summary\n\nthis is the agent prompt, not a log\n");

            var result = ContentView.BuildFailureCallout(CreateFailedPlan(planDir), tendrilHome);

            var callout = Assert.IsType<Callout>(result);
            Assert.Equal(CalloutVariant.Destructive, callout.Variant);
            Assert.Equal("Execution Failed", callout.Title);
        }
        finally
        {
            if (Directory.Exists(tendrilHome))
                Directory.Delete(tendrilHome, true);
        }
    }

    [Fact]
    public void BuildFailureCallout_WithSummaryLog_ShowsSummary()
    {
        var (tendrilHome, planDir) = CreateHome(
            "# Job Log 00007-00001-ExecutePlan\n\n## Summary\n\nBuild failed due to missing dependency.\n");
        try
        {
            var result = ContentView.BuildFailureCallout(CreateFailedPlan(planDir), tendrilHome);

            var callout = Assert.IsType<Callout>(result);
            Assert.Equal(CalloutVariant.Destructive, callout.Variant);
            Assert.Equal("Execution Failed", callout.Title);
        }
        finally
        {
            if (Directory.Exists(tendrilHome))
                Directory.Delete(tendrilHome, true);
        }
    }

    [Fact]
    public void BuildAnnotationsPrompt_NumbersAnnotationsAndQuotesSelectedText()
    {
        var annotations = new[]
        {
            new MarkdownAnnotation { SelectedText = "first passage", Comment = "make this clearer" },
            new MarkdownAnnotation { SelectedText = "second passage", Comment = "remove this requirement" }
        };

        var prompt = ContentView.BuildAnnotationsPrompt(annotations);

        Assert.Contains("## Annotation 1", prompt);
        Assert.Contains("> first passage", prompt);
        Assert.Contains("Comment: make this clearer", prompt);
        Assert.Contains("## Annotation 2", prompt);
        Assert.Contains("> second passage", prompt);
        Assert.Contains("Comment: remove this requirement", prompt);
    }

    [Fact]
    public void BuildAnnotationsPrompt_QuotesEveryLineOfMultilineSelection()
    {
        var annotations = new[]
        {
            new MarkdownAnnotation { SelectedText = "line one\r\nline two", Comment = "split this up" }
        };

        var prompt = ContentView.BuildAnnotationsPrompt(annotations);

        var lines = prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains("> line one", lines);
        Assert.Contains("> line two", lines);
    }

    [Fact]
    public void ValidateArtifactPath_WithValidPath_ReturnsTrue()
    {
        Assert.True(ReviewContentView.ValidateArtifactPath(
            "D:/plans/001/artifacts/screenshots/img.png", "D:/plans/001"));
    }

    [Fact]
    public void ValidateArtifactPath_WithTraversalPath_ReturnsFalse()
    {
        Assert.False(ReviewContentView.ValidateArtifactPath(
            "D:/plans/001/artifacts/../plan.yaml", "D:/plans/001"));
    }

    [Fact]
    public void ValidateArtifactPath_WithExternalPath_ReturnsFalse()
    {
        Assert.False(ReviewContentView.ValidateArtifactPath(
            "C:/Windows/System32/config", "D:/plans/001"));
    }

    [Fact]
    public void ValidateVerificationPath_WithValidName_ReturnsTrue()
    {
        Assert.True(ReviewContentView.ValidateVerificationPath(
            "DotnetBuild", "D:/plans/001"));
    }

    [Fact]
    public void ValidateVerificationPath_WithTraversalName_ReturnsFalse()
    {
        Assert.False(ReviewContentView.ValidateVerificationPath(
            "../../plan", "D:/plans/001"));
    }

    [Fact]
    public void ValidateVerificationPath_WithPathSeparator_ReturnsFalse()
    {
        Assert.False(ReviewContentView.ValidateVerificationPath(
            "../secrets/key", "D:/plans/001"));
    }

    [Fact]
    public void ResolvePendingSelection_WithPendingMatchingTitles_ReturnsThem()
    {
        var recs = new List<RecommendationYaml>
        {
            new() { Title = "R1" },
            new() { Title = "R2" },
            new() { Title = "R3" },
        };

        var selected = ReviewContentView.ResolvePendingSelection(recs, ["R1", "R3"]);

        Assert.Equal(["R1", "R3"], selected.Select(r => r.Title));
    }

    [Fact]
    public void ResolvePendingSelection_ExcludesNonPending()
    {
        var recs = new List<RecommendationYaml>
        {
            new() { Title = "R1", State = RecommendationStatus.Pending },
            new() { Title = "R2", State = RecommendationStatus.Accepted },
        };

        var selected = ReviewContentView.ResolvePendingSelection(recs, ["R1", "R2"]);

        Assert.Equal(["R1"], selected.Select(r => r.Title));
    }

    [Fact]
    public void ResolvePendingSelection_WithNoMatchingTitles_ReturnsEmpty()
    {
        var recs = new List<RecommendationYaml>
        {
            new() { Title = "R1" },
            new() { Title = "R2" },
        };

        var selected = ReviewContentView.ResolvePendingSelection(recs, ["Unknown"]);

        Assert.Empty(selected);
    }
}
