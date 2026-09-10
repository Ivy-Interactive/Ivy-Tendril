using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public class ChatAgentPreferences(IConfigService configService, ILogger<ChatAgentPreferences>? logger = null) : IChatAgentPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _lock = new();
    private Dictionary<string, ChatAgentPreference>? _preferences;

    private string FilePath => Path.Combine(configService.TendrilHome, "chat-agent-preferences.json");

    public ChatAgentPreference Get(string agentId)
    {
        lock (_lock)
        {
            return Load().TryGetValue(agentId, out var preference) ? preference : new ChatAgentPreference();
        }
    }

    public void SetModel(string agentId, string modelId) => Update(agentId, p => p with { ModelId = modelId });

    public void SetEffort(string agentId, string effort) => Update(agentId, p => p with { Effort = effort });

    private void Update(string agentId, Func<ChatAgentPreference, ChatAgentPreference> mutate)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;
        lock (_lock)
        {
            var preferences = Load();
            preferences[agentId] = mutate(preferences.TryGetValue(agentId, out var current) ? current : new ChatAgentPreference());
            Save(preferences);
        }
    }

    private Dictionary<string, ChatAgentPreference> Load()
    {
        if (_preferences != null) return _preferences;
        _preferences = new Dictionary<string, ChatAgentPreference>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, ChatAgentPreference>>(FileHelper.ReadAllText(FilePath), JsonOptions);
                foreach (var (agentId, preference) in loaded ?? [])
                {
                    _preferences[agentId] = preference;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to load chat agent preferences from {File}", FilePath);
        }
        return _preferences;
    }

    private void Save(Dictionary<string, ChatAgentPreference> preferences)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            FileHelper.WriteAllText(FilePath, JsonSerializer.Serialize(preferences, JsonOptions));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save chat agent preferences to {File}", FilePath);
        }
    }
}
