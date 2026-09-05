using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Services;

public interface IChatExecutionService : IDisposable
{
    event Action<string>? SessionGeneratingChanged;
    event Action<string>? StreamUpdated;
    bool IsGenerating(string sessionId);
    string GetStreamSnapshot(string sessionId);
    IObservable<string> GetLiveStreamObservable(string sessionId);
    Task SendMessageAsync(
        string sessionId,
        string prompt,
        IReadOnlyList<ChatAttachmentDto>? attachments = null,
        string? agentId = null,
        string? modelId = null,
        string? effort = null,
        CancellationToken ct = default);
    Task CancelAsync(string sessionId);
}
