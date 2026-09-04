using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public interface IChatSessionNamingService
{
    Task GenerateAndSetTitleAsync(
        string sessionId,
        string userPrompt,
        string assistantResponse,
        string? agentId = null,
        string? modelId = null,
        CancellationToken ct = default);
}

public class ChatSessionNamingService : IChatSessionNamingService
{
    private readonly IAgentRunner _agentRunner;
    private readonly IConfigService _configService;
    private readonly IChatHistoryService _chatHistoryService;
    private readonly ILogger<ChatSessionNamingService> _logger;

    public ChatSessionNamingService(
        IAgentRunner agentRunner,
        IConfigService configService,
        IChatHistoryService chatHistoryService,
        ILogger<ChatSessionNamingService> logger)
    {
        _agentRunner = agentRunner;
        _configService = configService;
        _chatHistoryService = chatHistoryService;
        _logger = logger;
    }

    public async Task GenerateAndSetTitleAsync(
        string sessionId,
        string userPrompt,
        string assistantResponse,
        string? agentId = null,
        string? modelId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) ||
            string.IsNullOrWhiteSpace(userPrompt) ||
            string.IsNullOrWhiteSpace(assistantResponse))
        {
            return;
        }

        try
        {
            var session = _chatHistoryService.GetSession(sessionId);
            if (session == null || !IsDefaultTitle(session.Title))
            {
                return;
            }

            var prompt = BuildPrompt(userPrompt, assistantResponse);
            var effectiveAgentId = !string.IsNullOrEmpty(agentId) ? agentId : (_configService.Settings.CodingAgent ?? "claude");

            var context = AgentLaunchHelper.PrepareResolutionContext(
                _configService,
                _agentRunner,
                effectiveAgentId,
                prompt,
                modelOverride: modelId,
                permissionMode: PermissionMode.FullAuto);

            context = context with
            {
                TimeoutPolicy = new TimeoutPolicy
                {
                    TotalTimeout = TimeSpan.FromSeconds(30),
                    StartupTimeout = TimeSpan.FromSeconds(15),
                    IdleTimeout = TimeSpan.FromSeconds(15),
                }
            };

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var result = await _agentRunner.RunToCompletionAsync(context, linkedCts.Token);

            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Response))
            {
                _logger.LogWarning("Failed to generate title for chat session {SessionId}: {Error}", sessionId, result.Error ?? "Empty response");
                return;
            }

            var cleanedTitle = CleanGeneratedTitle(result.Response);
            if (string.IsNullOrWhiteSpace(cleanedTitle) || IsDefaultTitle(cleanedTitle))
            {
                _logger.LogDebug("Generated title for chat session {SessionId} was empty or default after cleaning", sessionId);
                return;
            }

            // Verify the session still exists and has not been renamed by the user
            var latestSession = _chatHistoryService.GetSession(sessionId);
            if (latestSession != null && IsDefaultTitle(latestSession.Title))
            {
                _chatHistoryService.RenameSession(sessionId, cleanedTitle);
                _logger.LogInformation("Renamed chat session {SessionId} to '{Title}'", sessionId, cleanedTitle);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Title generation timed out for chat session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error generating title for chat session {SessionId}", sessionId);
        }
    }

    public static bool IsDefaultTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        var clean = title.Trim();
        return clean.Equals("New Chat", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildPrompt(string userPrompt, string assistantResponse)
    {
        return $"""
Generate a short 3 to 6 word title describing the conversation topic based on the following user message and assistant reply.
Output ONLY the title text. Do not include quotes, markdown headings, prefixes like "Title:", or trailing punctuation.

User:
{userPrompt}

Assistant:
{assistantResponse}
""";
    }

    public static string? CleanGeneratedTitle(string? rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse)) return null;

        // Take the first non-empty line
        var lines = rawResponse.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (string.IsNullOrWhiteSpace(firstLine)) return null;

        var title = firstLine.Trim();

        string[] prefixes = ["Title:", "title:", "TITLE:", "Topic:", "topic:", "TOPIC:", "Subject:", "subject:", "Name:", "name:"];

        string previous;
        do
        {
            previous = title;

            // Strip markdown headings: #, ##, ###, etc.
            while (title.StartsWith('#'))
            {
                title = title.TrimStart('#').TrimStart();
            }

            // Strip bold/italic/code markdown formatting
            title = StripWrappingFormatting(title);

            // Strip prefixes
            foreach (var prefix in prefixes)
            {
                if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    title = title[prefix.Length..].Trim();
                    break;
                }
            }

            // Strip surrounding quotes
            title = title.Trim('"', '\'', '`', '“', '”', '«', '»');

            // Strip trailing punctuation: ., ..., …, !, ?, :, ;, etc.
            while (title.EndsWith("...") || title.EndsWith("…"))
            {
                if (title.EndsWith("..."))
                    title = title[..^3].TrimEnd();
                else if (title.EndsWith("…"))
                    title = title[..^1].TrimEnd();
            }
            title = title.TrimEnd('.', '!', '?', ':', ';', ',');

            // Strip surrounding quotes again
            title = title.Trim('"', '\'', '`', '“', '”', '«', '»');

            title = title.Trim();
        } while (title != previous && !string.IsNullOrWhiteSpace(title));

        if (string.IsNullOrWhiteSpace(title)) return null;

        // Enforce maximum length of 50 characters
        if (title.Length > 50)
        {
            title = title[..50].Trim();
        }

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string StripWrappingFormatting(string text)
    {
        var result = text.Trim();
        if (result.StartsWith("**") && result.EndsWith("**") && result.Length >= 4)
        {
            result = result[2..^2].Trim();
        }
        else if (result.StartsWith('*') && result.EndsWith('*') && result.Length >= 2)
        {
            result = result[1..^1].Trim();
        }
        else if (result.StartsWith('`') && result.EndsWith('`') && result.Length >= 2)
        {
            result = result[1..^1].Trim();
        }
        return result;
    }
}
