using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Apps.Chat;

internal static class SamplePrompts
{
    public const int Max = 4;

    public static List<ChatSamplePromptDto> ForChat(
        IReadOnlyList<PlanFile> plans,
        IReadOnlyList<JobItem> runningJobs)
    {
        var prompts = new List<ChatSamplePromptDto>();
        var labels = new HashSet<string>();

        // Rule: any plan in Review
        var reviewPlans = plans.Where(p => p.Status == PlanStatus.Review).ToList();
        if (reviewPlans.Count > 0)
        {
            var label = $"Review the {reviewPlans.Count} plans waiting";
            if (labels.Add(label))
            {
                var planList = string.Join(", ", reviewPlans.Select(p => $"#{p.Id} {p.Title}"));
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"{reviewPlans.Count} plans are waiting for review: {planList}. Summarize what each delivers and tell me which to merge first."));
            }
        }

        // Rule: any plan Failed, newest by Updated
        var failedPlan = plans
            .Where(p => p.Status == PlanStatus.Failed)
            .OrderByDescending(p => p.Updated)
            .FirstOrDefault();
        if (failedPlan != null)
        {
            var label = $"Why did #{failedPlan.Id} fail?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"Plan #{failedPlan.Id} {failedPlan.Title} failed. Read its logs and verification reports and explain what went wrong."));
            }
        }

        // Rule: any plan Blocked, newest
        var blockedPlan = plans
            .Where(p => p.Status == PlanStatus.Blocked)
            .OrderByDescending(p => p.Updated)
            .FirstOrDefault();
        if (blockedPlan != null)
        {
            var label = $"What is blocking #{blockedPlan.Id}?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"Plan #{blockedPlan.Id} {blockedPlan.Title} is blocked. List the plans it depends on and what each one still needs."));
            }
        }

        // Rule: runningJobs not empty
        if (runningJobs.Count > 0)
        {
            var label = "What are my jobs doing?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"{runningJobs.Count} jobs are running. Summarize what each one is working on."));
            }
        }

        // Rule: any plan with PartialDelivery, newest
        var partialPlan = plans
            .Where(p => p.PartialDelivery)
            .OrderByDescending(p => p.Updated)
            .FirstOrDefault();
        if (partialPlan != null)
        {
            var label = $"What did #{partialPlan.Id} skip?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"Plan #{partialPlan.Id} {partialPlan.Title} completed over a failed verification. Tell me what it did not deliver."));
            }
        }

        // Fallbacks
        var fallback1 = "What should I work on next?";
        if (labels.Add(fallback1))
        {
            prompts.Add(new ChatSamplePromptDto(
                fallback1,
                "Look at my draft plans across all projects and recommend which two to execute next, with reasons."));
        }

        var fallback2 = "What shipped this week?";
        if (labels.Add(fallback2))
        {
            prompts.Add(new ChatSamplePromptDto(
                fallback2,
                "Summarize the plans that reached Completed in the last seven days, grouped by project."));
        }

        return prompts.Take(Max).ToList();
    }

    public static List<ChatSamplePromptDto> ForPlan(PlanFile plan)
    {
        var prompts = new List<ChatSamplePromptDto>();
        var labels = new HashSet<string>();

        // Rule: a verification is Fail, first in list order
        var failedVerification = plan.Verifications
            .FirstOrDefault(v => v.Status == VerificationStatus.Fail);
        if (failedVerification != null)
        {
            var label = $"Why did {failedVerification.Name} fail?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"The {failedVerification.Name} verification failed for this plan. Read its report in Verification/{failedVerification.Name}.md and explain the failure and how to fix it."));
            }
        }

        // Rule: plan.PartialDelivery
        if (plan.PartialDelivery)
        {
            var label = "What is missing?";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    "This plan completed over a failed verification. Tell me what it did not deliver."));
            }
        }

        // Rule: plan.Prs not empty
        if (plan.Prs.Count > 0)
        {
            var label = "Summarize the PR feedback";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"Read the review comments on {plan.Prs[0]} and list the changes they ask for."));
            }
        }

        // Rule: plan.Status is Blocked
        if (plan.Status == PlanStatus.Blocked)
        {
            var label = "What is blocking this?";
            if (labels.Add(label))
            {
                var deps = string.Join(", ", plan.DependsOn);
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    $"This plan is blocked on {deps}. Tell me what each dependency still needs."));
            }
        }

        // Rule: plan.Status is Draft
        if (plan.Status == PlanStatus.Draft)
        {
            var label = "Tighten the scope";
            if (labels.Add(label))
            {
                prompts.Add(new ChatSamplePromptDto(
                    label,
                    "Read the latest revision of this plan and point out anything out of scope or under specified."));
            }
        }

        // Fallbacks
        var fallback1 = "Explain the solution";
        if (labels.Add(fallback1))
        {
            prompts.Add(new ChatSamplePromptDto(
                fallback1,
                "Explain the Solution section of this plan in plain terms, and list every file it will touch."));
        }

        var fallback2 = "What could go wrong?";
        if (labels.Add(fallback2))
        {
            prompts.Add(new ChatSamplePromptDto(
                fallback2,
                "What are the riskiest parts of this plan, and what should I check in review?"));
        }

        return prompts.Take(Max).ToList();
    }
}
