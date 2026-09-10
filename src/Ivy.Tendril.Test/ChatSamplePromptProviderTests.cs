using System;
using System.Collections.Generic;
using System.IO;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Chat;
using Xunit;

namespace Ivy.Tendril.Test;

public class ChatSamplePromptProviderTests
{
    private static PlanFile CreatePlan(
        int id,
        string title,
        PlanStatus status = PlanStatus.Draft,
        string latestRevision = "",
        List<string>? dependsOn = null,
        List<PlanVerificationEntry>? verifications = null)
    {
        var folderName = $"{id:D5}-{title.Replace(' ', '-')}";
        var metadata = new PlanMetadata(
            Id: id,
            Project: "Acme",
            Level: "Feature",
            Title: title,
            State: status,
            Repos: [],
            Commits: [],
            Prs: [],
            Verifications: verifications ?? [],
            RelatedPlans: [],
            DependsOn: dependsOn ?? [],
            Created: DateTime.UtcNow,
            Updated: DateTime.UtcNow,
            InitialPrompt: null,
            SourceUrl: null,
            ChatSessionId: null);

        return new PlanFile(metadata, latestRevision, $"/tmp/Plans/{folderName}", "");
    }

    [Fact]
    public void GetPromptsForPlan_DraftPlan_IncludesSolutionApproachAndTestCoverage()
    {
        var plan = CreatePlan(1, "Draft feature", PlanStatus.Draft);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Explain the solution approach for this plan", prompts);
        Assert.Contains("Add unit tests to the plan's test section", prompts);
        Assert.Contains("What are the risks and dependencies of this approach?", prompts);
        Assert.Contains("Make this plan more concise", prompts);
        Assert.DoesNotContain("Help me answer the open questions", prompts);
        Assert.DoesNotContain("What dependencies block this plan?", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_DraftPlan_WithOpenQuestions_IncludesAnswerOpenQuestions()
    {
        var revisionMarkdown = @"# Sample Plan

```questions
questions:
  - id: choice-1
    title: Which strategy to choose?
    options:
      - title: Option A
        value: option-a
      - title: Option B
        value: option-b
```
";
        var plan = CreatePlan(2, "Plan with questions", PlanStatus.Draft, latestRevision: revisionMarkdown);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Help me answer the open questions", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_DraftPlan_WithAnsweredQuestions_DoesNotIncludeAnswerOpenQuestions()
    {
        var revisionMarkdown = @"# Sample Plan

```questions
questions:
  - id: choice-1
    title: Which strategy to choose?
    options:
      - title: Option A
        value: option-a
      - title: Option B
        value: option-b
    answer: option-a
```
";
        var plan = CreatePlan(3, "Plan with answered questions", PlanStatus.Draft, latestRevision: revisionMarkdown);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.DoesNotContain("Help me answer the open questions", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_DraftPlan_WithDependsOn_IncludesDependencyQuestion()
    {
        var plan = CreatePlan(4, "Plan with dependencies", PlanStatus.Draft, dependsOn: ["00100-BasePlan"]);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("What dependencies block this plan?", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_ReviewPlan_IncludesDiffReviewVerificationCheckAndPrPrompts()
    {
        var plan = CreatePlan(5, "Review feature", PlanStatus.Review);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Explain the implementation changes made", prompts);
        Assert.Contains("Review the git diff for potential issues", prompts);
        Assert.Contains("Create the pull request", prompts);
        Assert.Contains("Retry the plan with adjustments", prompts);
        Assert.DoesNotContain("Why did verification fail?", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_ReviewPlan_WithFailedVerification_IncludesWhyVerificationFailed()
    {
        var verifications = new List<PlanVerificationEntry>
        {
            new() { Name = "DotnetBuild", Status = VerificationStatus.Pass },
            new() { Name = "DotnetTest", Status = VerificationStatus.Fail }
        };
        var plan = CreatePlan(6, "Review feature failing", PlanStatus.Review, verifications: verifications);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Why did verification fail?", prompts);
        Assert.Contains("Retry the plan with adjustments", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_FailedPlan_IncludesFailureDiagnosis()
    {
        var plan = CreatePlan(7, "Failed feature", PlanStatus.Failed);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Why did the plan execution fail?", prompts);
        Assert.Contains("Retry the plan with fixes for the errors", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_CompletedPlan_IncludesCompletionSummaryAndPrDetails()
    {
        var plan = CreatePlan(8, "Completed feature", PlanStatus.Completed);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Summarize what was delivered in this plan", prompts);
        Assert.Contains("Review the pull request details", prompts);
    }

    [Fact]
    public void GetPromptsForPlan_DefaultOrOtherState_ReturnsFallbackPrompts()
    {
        var plan = CreatePlan(9, "Executing feature", PlanStatus.Executing);

        var prompts = ChatSamplePromptProvider.GetPromptsForPlan(plan);

        Assert.Contains("Explain the solution approach", prompts);
        Assert.Contains("What is the status of this plan?", prompts);
    }

    [Fact]
    public void GetPromptsForGeneralChat_WithRegisteredProject_IncludesProjectAwarePrompts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "TendrilSamplePromptsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new TendrilSettings();
            var config = new ConfigService(settings, tempDir);
            config.Projects.Add(new ProjectConfig { Name = "awesome-app" });

            var prompts = ChatSamplePromptProvider.GetPromptsForGeneralChat(config);

            Assert.Contains("Create a plan to add <feature> to awesome-app", prompts);
            Assert.Contains("What plans are in Draft or Review?", prompts);
            Assert.Contains("Review recent job failures", prompts);
            Assert.Contains("Explain the architecture and tech stack of awesome-app", prompts);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetPromptsForGeneralChat_WithoutProject_FallsBackToGenericPrompts()
    {
        var prompts = ChatSamplePromptProvider.GetPromptsForGeneralChat(null);

        Assert.Contains("Create a new plan", prompts);
        Assert.Contains("What plans are in Draft or Review?", prompts);
        Assert.Contains("Review recent job failures", prompts);
        Assert.Contains("Explain the architecture and tech stack", prompts);
    }

    [Fact]
    public void ContentView_ExposesSamplePromptsWhenSupplied()
    {
        var samplePrompts = new List<string> { "Prompt A", "Prompt B" };

        var contentView = new ContentView(
            activeSession: null,
            activeSessionId: new FakeState<string?>(null),
            sessionVersion: new FakeState<int>(0),
            selectedAgent: new FakeState<string>("claude"),
            selectedModel: new FakeState<string>("opus"),
            selectedEffort: new FakeState<string>("default"),
            sessionDtos: [],
            agentDtos: [],
            modelDtos: [],
            effortDtos: [],
            supportsEffort: false,
            isStreaming: false,
            streamingText: "",
            greeting: "Hello",
            headline: "Headline",
            chatService: null!,
            executionService: null!,
            agentRunner: null!,
            sendMessage: _ => { },
            selectSession: _ => { },
            startNewChat: () => { },
            embedded: false,
            samplePrompts: samplePrompts
        );

        Assert.NotNull(contentView.SamplePrompts);
        Assert.Equal(2, contentView.SamplePrompts.Count);
        Assert.Equal("Prompt A", contentView.SamplePrompts[0]);
    }

    private sealed class FakeState<T> : Ivy.IState<T>
    {
        private readonly T _initial;

        public FakeState(T initial)
        {
            _initial = initial;
            Value = initial;
        }

        public T Value { get; set; }
        public IDisposable Subscribe(IObserver<T> observer) => throw new NotImplementedException();
        public void Dispose() { }
        public T Set(T value) => Value = value;
        public T Set(Func<T, T> setter) => Value = setter(Value);
        public T Reset() => Value = _initial;
        public IDisposable SubscribeAny(Action action) => throw new NotImplementedException();
        public IDisposable SubscribeAny(Action<object?> action) => throw new NotImplementedException();
        public Type GetStateType() => typeof(T);
        public object? GetValueAsObject() => Value;
        public Ivy.IEffectTrigger ToTrigger() => throw new NotImplementedException();
    }
}
