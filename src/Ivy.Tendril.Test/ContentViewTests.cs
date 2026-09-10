using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Apps.Plans.Dialogs;
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
    public void BuildUpdatePrompt_NumbersAnnotationsAndQuotesSelectedText()
    {
        var annotations = new[]
        {
            new MarkdownAnnotation { SelectedText = "first passage", Comment = "make this clearer" },
            new MarkdownAnnotation { SelectedText = "second passage", Comment = "remove this requirement" }
        };

        var prompt = ContentView.BuildUpdatePrompt(annotations);

        Assert.Contains("## Annotation 1", prompt);
        Assert.Contains("> first passage", prompt);
        Assert.Contains("Comment: make this clearer", prompt);
        Assert.Contains("## Annotation 2", prompt);
        Assert.Contains("> second passage", prompt);
        Assert.Contains("Comment: remove this requirement", prompt);
    }

    [Fact]
    public void BuildUpdatePrompt_QuotesEveryLineOfMultilineSelection()
    {
        var annotations = new[]
        {
            new MarkdownAnnotation { SelectedText = "line one\r\nline two", Comment = "split this up" }
        };

        var prompt = ContentView.BuildUpdatePrompt(annotations);

        var lines = prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Contains("> line one", lines);
        Assert.Contains("> line two", lines);
    }

    [Fact]
    public void BuildUpdatePrompt_TellsTheAgentAnswersAreAlreadyInTheRevision()
    {
        // The answers are not quoted into the prompt — they are already in the file the agent reads.
        var prompt = ContentView.BuildUpdatePrompt([], answeredQuestions: 2);

        Assert.Contains("I answered 2 questions", prompt);
        Assert.Contains("already in the revision", prompt);
        Assert.Contains("delete that", prompt);
        // A question left alone must survive the update untouched.
        Assert.Contains("Carry any question I left unanswered forward unchanged", prompt);
        Assert.DoesNotContain("## Annotation", prompt);
    }

    [Fact]
    public void BuildUpdatePrompt_UsesSingularForOneAnsweredQuestion()
    {
        Assert.Contains("I answered 1 question in", ContentView.BuildUpdatePrompt([], answeredQuestions: 1));
    }

    [Fact]
    public void BuildUpdatePrompt_CarriesAnnotationsAndAnswersTogether()
    {
        var annotations = new[]
        {
            new MarkdownAnnotation { SelectedText = "a passage", Comment = "tighten this" }
        };

        var prompt = ContentView.BuildUpdatePrompt(annotations, answeredQuestions: 1);

        Assert.Contains("I answered 1 question", prompt);
        Assert.Contains("## Annotation 1", prompt);
        Assert.Contains("Comment: tighten this", prompt);
    }

    [Fact]
    public void BuildUpdatePrompt_WithNothingPendingIsEmpty()
    {
        Assert.Equal(string.Empty, ContentView.BuildUpdatePrompt([]));
    }

    [Theory]
    // Annotations are discarded by declining; answers are not, so the wording differs.
    [InlineData(2, 0, "2 annotations", "would ignore them")]
    [InlineData(0, 3, "3 answered questions", "as they stand")]
    [InlineData(1, 1, "1 annotation and 1 answered question", "ignore the annotations")]
    public void PendingAnnotationsDialog_MessageNamesWhatIsOutstanding(
        int annotations, int answers, string expectedCount, string expectedConsequence)
    {
        var message = PendingAnnotationsDialog.Message(annotations, answers);

        Assert.Contains(expectedCount, message);
        Assert.Contains(expectedConsequence, message);
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

    [Fact]
    public void CountUnansweredQuestions_IncludesOptionalQuestionsWithoutAnswers()
    {
        var questions = new QuestionSummary[]
        {
            new(0, "q1", "Question 1", null, HasAnswer: false, IsOptional: false),
            new(0, "q2", "Question 2", null, HasAnswer: false, IsOptional: true),
            new(0, "q3", "Question 3", null, HasAnswer: true, IsOptional: false),
            new(0, "q4", "Question 4", null, HasAnswer: true, IsOptional: true),
        };

        var count = ContentView.CountUnansweredQuestions(questions);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountUnansweredQuestions_FromMarkdown_CountsBothRequiredAndOptionalUnanswered()
    {
        var markdown = """
            # Test Plan

            ```questions
            - id: req-q
              question: "Required question"
              options:
                - text: "Option A"
                - text: "Option B"
            - id: opt-q
              question: "Optional question"
              optional: true
              options:
                - text: "Option A"
                - text: "Option B"
            - id: answered-opt-q
              question: "Answered optional question"
              optional: true
              answer: "Option A"
              options:
                - text: "Option A"
                - text: "Option B"
            ```
            """;

        var count = ContentView.CountUnansweredQuestions(markdown);

        Assert.Equal(2, count);
    }
}
