using System;
using System.Collections.Generic;
using System.Linq;
using Ivy.Tendril.Models;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services.Chat;

public static class ChatSamplePromptProvider
{
    public static List<string> GetPromptsForPlan(PlanFile plan)
    {
        if (plan == null) return [];

        var prompts = new List<string>();

        switch (plan.Status)
        {
            case PlanStatus.Draft:
                prompts.Add("Explain the solution approach for this plan");
                prompts.Add("Add unit tests to the plan's test section");
                prompts.Add("What are the risks and dependencies of this approach?");
                prompts.Add("Make this plan more concise");

                if (!string.IsNullOrWhiteSpace(plan.LatestRevisionContent))
                {
                    var questions = QuestionAnswers.Read(plan.LatestRevisionContent);
                    if (questions.Any(q => !q.HasAnswer))
                    {
                        prompts.Add("Help me answer the open questions");
                    }
                }

                if (plan.DependsOn != null && plan.DependsOn.Count > 0)
                {
                    prompts.Add("What dependencies block this plan?");
                }
                break;

            case PlanStatus.Review:
                prompts.Add("Explain the implementation changes made");
                prompts.Add("Review the git diff for potential issues");
                prompts.Add("Create the pull request");

                var hasFailedVerification = plan.Verifications != null &&
                    plan.Verifications.Any(v => v.Status == VerificationStatus.Fail);
                if (hasFailedVerification)
                {
                    prompts.Add("Why did verification fail?");
                }

                prompts.Add("Retry the plan with adjustments");
                break;

            case PlanStatus.Failed:
                prompts.Add("Why did the plan execution fail?");
                prompts.Add("Retry the plan with fixes for the errors");
                break;

            case PlanStatus.Completed:
                prompts.Add("Summarize what was delivered in this plan");
                prompts.Add("Review the pull request details");
                break;

            default:
                prompts.Add("Explain the solution approach");
                prompts.Add("What is the status of this plan?");
                break;
        }

        return prompts;
    }

    public static List<string> GetPromptsForGeneralChat(
        IConfigService? configService = null,
        IPlanReaderService? planService = null,
        IJobService? jobService = null)
    {
        var prompts = new List<string>();
        var primaryProject = configService?.Projects?.FirstOrDefault()?.Name;

        if (!string.IsNullOrWhiteSpace(primaryProject))
        {
            prompts.Add($"Create a plan to add <feature> to {primaryProject}");
        }
        else
        {
            prompts.Add("Create a new plan");
        }

        prompts.Add("What plans are in Draft or Review?");
        prompts.Add("Review recent job failures");

        if (!string.IsNullOrWhiteSpace(primaryProject))
        {
            prompts.Add($"Explain the architecture and tech stack of {primaryProject}");
        }
        else
        {
            prompts.Add("Explain the architecture and tech stack");
        }

        return prompts;
    }
}
