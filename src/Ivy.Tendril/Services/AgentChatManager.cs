using System;
using System.Collections.Concurrent;

namespace Ivy.Tendril.Services;

public interface IAgentChatManager
{
    int ActiveChatsCount { get; }
    IDisposable RegisterActiveChat(string chatId);
    event Action? ActiveChatsChanged;
}

public class AgentChatManager : IAgentChatManager
{
    private readonly ConcurrentDictionary<string, byte> _activeChats = new();

    public int ActiveChatsCount => _activeChats.Count;

    public event Action? ActiveChatsChanged;

    public IDisposable RegisterActiveChat(string chatId)
    {
        _activeChats[chatId] = 0;
        ActiveChatsChanged?.Invoke();

        return System.Reactive.Disposables.Disposable.Create(() =>
        {
            _activeChats.TryRemove(chatId, out _);
            ActiveChatsChanged?.Invoke();
        });
    }
}
