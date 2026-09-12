using System;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Apps.Chat;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatSamplePromptsTests
{
    [Fact]
    public void ForChat_NamesThePlansWaitingForReview()
    {
        var now = DateTime.UtcNow;
        var plans = new List<PlanFile>
        {
            CreatePlanFile(101, "Add Login", PlanStatus.Review, updated: now),
            CreatePlanFile(102, "Fix Bug", PlanStatus.Review, updated: now),
            CreatePlanFile(103, "Draft Plan", PlanStatus.Draft, updated: now),
        };

        var prompts = SamplePrompts.ForChat(plans, new List<JobItem>());

        var reviewPrompt = prompts.FirstOrDefault(p => p.Label.Contains("Review"));
        Assert.NotNull(reviewPrompt);
        Assert.Contains("2 plans", reviewPrompt.Label);
        Assert.Contains("#101", reviewPrompt.Prompt);
        Assert.Contains("#102", reviewPrompt.Prompt);
        Assert.DoesNotContain("#103", reviewPrompt.Prompt);
    }

    [Fact]
    public void ForChat_NamesTheNewestFailedPlan()
    {
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);
        var plans = new List<PlanFile>
        {
            CreatePlanFile(201, "Old Failed", PlanStatus.Failed, updated: older),
            CreatePlanFile(202, "New Failed", PlanStatus.Failed, updated: newer),
        };

        var prompts = SamplePrompts.ForChat(plans, new List<JobItem>());

        var failedPrompt = prompts.FirstOrDefault(p => p.Label.Contains("fail"));
        Assert.NotNull(failedPrompt);
        Assert.Contains("#202", failedPrompt.Label);
        Assert.DoesNotContain("#201", failedPrompt.Label);
        Assert.Contains("New Failed", failedPrompt.Prompt);
    }

    [Fact]
    public void ForChat_ReportsRunningJobs()
    {
        var jobs = new List<JobItem>
        {
            new() { Id = "job1", Status = JobStatus.Running },
            new() { Id = "job2", Status = JobStatus.Pending },
        };

        var prompts = SamplePrompts.ForChat(new List<PlanFile>(), jobs);

        var jobPrompt = prompts.FirstOrDefault(p => p.Label.Contains("jobs"));
        Assert.NotNull(jobPrompt);
        Assert.Contains("2 jobs", jobPrompt.Prompt);
    }

    [Fact]
    public void ForChat_FallsBackWhenThereIsNoLiveContext()
    {
        var prompts = SamplePrompts.ForChat(new List<PlanFile>(), new List<JobItem>());

        Assert.Equal(2, prompts.Count);
        Assert.Contains(prompts, p => p.Label == "What should I work on next?");
        Assert.Contains(prompts, p => p.Label == "What shipped this week?");
    }

    [Fact]
    public void ForChat_CapsTheListAtFourAndIsDeterministic()
    {
        var now = DateTime.UtcNow;
        var plans = new List<PlanFile>
        {
            CreatePlanFile(1, "Review", PlanStatus.Review, updated: now),
            CreatePlanFile(2, "Failed", PlanStatus.Failed, updated: now),
            CreatePlanFile(3, "Blocked", PlanStatus.Blocked, updated: now),
            CreatePlanFile(4, "Partial", PlanStatus.Draft, updated: now, partialDelivery: true),
        };
        var jobs = new List<JobItem>
        {
            new() { Id = "job1", Status = JobStatus.Running },
        };

        var prompts1 = SamplePrompts.ForChat(plans, jobs);
        var prompts2 = SamplePrompts.ForChat(plans, jobs);

        Assert.Equal(SamplePrompts.Max, prompts1.Count);
        Assert.Equal(prompts1.Select(p => p.Label), prompts2.Select(p => p.Label));
    }

    [Fact]
    public void ForPlan_NamesTheFailedVerification()
    {
        var plan = CreatePlanFile(
            301,
            "Test Plan",
            verifications: new List<PlanVerificationEntry>
            {
                new() { Name = "Build", Status = VerificationStatus.Pass },
                new() { Name = "Test", Status = VerificationStatus.Fail },
                new() { Name = "Lint", Status = VerificationStatus.Pending },
            });

        var prompts = SamplePrompts.ForPlan(plan);

        var failedPrompt = prompts.FirstOrDefault(p => p.Label.Contains("Test"));
        Assert.NotNull(failedPrompt);
        Assert.Contains("Test verification failed", failedPrompt.Prompt);
    }

    [Fact]
    public void ForPlan_AsksAboutThePullRequestWhenThePlanHasOne()
    {
        var plan = CreatePlanFile(
            401,
            "PR Plan",
            prs: new List<string> { "https://github.com/owner/repo/pull/123" });

        var prompts = SamplePrompts.ForPlan(plan);

        var prPrompt = prompts.FirstOrDefault(p => p.Label.Contains("PR feedback"));
        Assert.NotNull(prPrompt);
        Assert.Contains("https://github.com/owner/repo/pull/123", prPrompt.Prompt);
    }

    [Fact]
    public void ForPlan_FallsBackForAFreshDraftPlan()
    {
        var plan = CreatePlanFile(
            501,
            "Draft Plan",
            status: PlanStatus.Draft,
            verifications: new List<PlanVerificationEntry>());

        var prompts = SamplePrompts.ForPlan(plan);

        Assert.Contains(prompts, p => p.Label == "Tighten the scope");
        Assert.Contains(prompts, p => p.Label == "Explain the solution");
    }

    [Fact]
    public void ForPlan_ProducesNoDuplicateLabels()
    {
        var plan = CreatePlanFile(
            601,
            "Complex Plan",
            status: PlanStatus.Draft,
            verifications: new List<PlanVerificationEntry>
            {
                new() { Name = "Test", Status = VerificationStatus.Fail },
            });

        var prompts = SamplePrompts.ForPlan(plan);

        var labels = prompts.Select(p => p.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    private static PlanFile CreatePlanFile(
        int id,
        string title,
        PlanStatus status = PlanStatus.Draft,
        DateTime? updated = null,
        bool partialDelivery = false,
        List<PlanVerificationEntry>? verifications = null,
        List<string>? prs = null)
    {
        var metadata = new PlanMetadata(
            id, "test-project", "Feature", title, status,
            [], [], prs ?? [], verifications ?? [], [], [],
            DateTime.UtcNow, updated ?? DateTime.UtcNow, null, null,
            PartialDelivery: partialDelivery);
        return new PlanFile(metadata, "", $"/tmp/{id:D5}-{title}", "");
    }
}
