using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Announces manual approval actions (such as starting ExecutePlan, CreatePr, or Update PR) to the linked chat session
///     and resolves pending questions in that session.
/// </summary>
public static class ManualApprovalAnnouncer
{
    public static void AnnounceExecution(
        PlanFile? plan,
        string jobId,
        IChatHistoryService? chatHistory,
        IChatExecutionService? chatExecution,
        IJobService? jobService = null)
    {
        AnnounceApproval(plan, jobId, isPr: false, isPrUpdate: false, chatHistory, chatExecution, jobService);
    }

    public static void AnnounceCreatePr(
        PlanFile? plan,
        string jobId,
        bool isPrUpdate,
        IChatHistoryService? chatHistory,
        IChatExecutionService? chatExecution,
        IJobService? jobService = null)
    {
        AnnounceApproval(plan, jobId, isPr: true, isPrUpdate: isPrUpdate, chatHistory, chatExecution, jobService);
    }

    private static void AnnounceApproval(
        PlanFile? plan,
        string jobId,
        bool isPr,
        bool isPrUpdate,
        IChatHistoryService? chatHistory,
        IChatExecutionService? chatExecution,
        IJobService? jobService)
    {
        if (plan is null) return;

        var chatSessionId = plan.ChatSessionId;
        if (chatHistory != null)
        {
            if (string.IsNullOrEmpty(chatSessionId) || chatHistory.GetSession(chatSessionId) == null)
            {
                var jobChatId = jobService?.GetJob(jobId)?.ChatSessionId;
                if (!string.IsNullOrEmpty(jobChatId) && chatHistory.GetSession(jobChatId) != null)
                {
                    chatSessionId = jobChatId;
                }
            }

            if (string.IsNullOrEmpty(chatSessionId) || chatHistory.GetSession(chatSessionId) == null)
            {
                if (!string.IsNullOrEmpty(plan.FolderName))
                {
                    var fallback = chatHistory.GetSessions()
                        .Where(s => string.Equals(s.PlanFolderName, plan.FolderName, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.UpdatedAt)
                        .FirstOrDefault();
                    if (fallback != null)
                    {
                        chatSessionId = fallback.Id;
                    }
                }
            }
        }
        else
        {
            if (string.IsNullOrEmpty(chatSessionId))
            {
                chatSessionId = jobService?.GetJob(jobId)?.ChatSessionId;
            }
        }

        if (string.IsNullOrEmpty(chatSessionId)) return;

        if (chatHistory != null)
        {
            try
            {
                var session = chatHistory.GetSession(chatSessionId);
                if (session?.Messages != null)
                {
                    foreach (var msg in session.Messages)
                    {
                        if (string.IsNullOrWhiteSpace(msg.Content)) continue;

                        var summaries = QuestionAnswers.Read(msg.Content);
                        var unanswered = summaries.Where(q => !q.HasAnswer).ToList();
                        if (unanswered.Count == 0) continue;

                        var parsedBlocks = QuestionBlockParser.Parse(msg.Content);
                        var answersToApply = new Dictionary<string, string[]>();

                        foreach (var qSummary in unanswered)
                        {
                            if (!IsMatchingQuestion(qSummary, isPr)) continue;

                            PlanQuestion? matchedQuestion = null;
                            foreach (var pb in parsedBlocks)
                            {
                                if (pb.Block?.Questions != null)
                                {
                                    matchedQuestion = pb.Block.Questions.FirstOrDefault(q => q.Id == qSummary.Id);
                                    if (matchedQuestion != null) break;
                                }
                            }

                            if (matchedQuestion?.Options is { Count: > 0 } options)
                            {
                                var chosenOption = MatchOption(options, isPr);
                                answersToApply[qSummary.Id] = [chosenOption.Value];
                            }
                        }

                        if (answersToApply.Count > 0)
                        {
                            chatHistory.ApplyQuestionAnswers(chatSessionId, msg.Id, answersToApply);
                        }
                    }
                }
            }
            catch
            {
                // Gracefully handle question resolution errors
            }
        }

        if (chatExecution is null) return;

        var message = !isPr
            ? $"[System Event] Manual approval granted and execution started for plan '{plan.Title}' (Job {jobId})."
            : isPrUpdate
                ? $"[System Event] Manual approval granted and Update PR started for plan '{plan.Title}' (Job {jobId})."
                : $"[System Event] Manual approval granted and Create PR started for plan '{plan.Title}' (Job {jobId}).";

        _ = Task.Run(async () =>
        {
            try
            {
                await chatExecution.SendMessageAsync(chatSessionId, message, role: "system");
            }
            catch
            {
                // Gracefully handle dispatch errors
            }
        });
    }

    private static bool IsMatchingQuestion(QuestionSummary qSummary, bool isPr)
    {
        if (isPr)
        {
            return qSummary.Id.Contains("pr", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Id.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Id.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Id.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Id.Contains("merge", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Title.Contains("pr", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Title.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                   qSummary.Title.Contains("merge", StringComparison.OrdinalIgnoreCase);
        }

        return qSummary.Id.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
               qSummary.Id.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
               qSummary.Id.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
               qSummary.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
               qSummary.Title.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
               qSummary.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase);
    }

    private static QuestionOption MatchOption(IReadOnlyList<QuestionOption> options, bool isPr)
    {
        var recommended = options.FirstOrDefault(o => o.Recommended);
        if (recommended != null) return recommended;

        if (isPr)
        {
            return options.FirstOrDefault(o =>
                o.Value.Contains("pr", StringComparison.OrdinalIgnoreCase) ||
                o.Value.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                o.Value.Contains("create", StringComparison.OrdinalIgnoreCase) ||
                o.Value.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                o.Value.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                o.Value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("pr", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("pull", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("create", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
                o.Title.Contains("yes", StringComparison.OrdinalIgnoreCase)) ?? options[0];
        }

        return options.FirstOrDefault(o =>
            o.Value.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
            o.Value.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
            o.Value.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
            o.Value.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
            o.Title.Contains("approv", StringComparison.OrdinalIgnoreCase) ||
            o.Title.Contains("execut", StringComparison.OrdinalIgnoreCase) ||
            o.Title.Contains("proceed", StringComparison.OrdinalIgnoreCase) ||
            o.Title.Contains("yes", StringComparison.OrdinalIgnoreCase)) ?? options[0];
    }
}
