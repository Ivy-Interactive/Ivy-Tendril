using System;
using System.IO;
using Ivy.Tendril.Services;
using Xunit;

namespace Ivy.Tendril.Test.Apps.Chat;

public class ChatAgentPreferencesTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "TendrilChatPrefs_" + Guid.NewGuid().ToString("N"));

    public ChatAgentPreferencesTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private ChatAgentPreferences Create() => new(new ConfigService(new TendrilSettings(), _tempDir));

    [Fact]
    public void Get_ReturnsAnEmptyPreferenceForAnUnknownAgent()
    {
        var preference = Create().Get("codex");

        Assert.Null(preference.ModelId);
        Assert.Null(preference.Effort);
    }

    [Fact]
    public void SetModelAndEffort_AreKeptPerAgentAndSurviveANewInstance()
    {
        var preferences = Create();

        preferences.SetModel("claude", "sonnet");
        preferences.SetEffort("claude", "max");
        preferences.SetModel("codex", "gpt-5");

        var reloaded = Create();
        Assert.Equal(("sonnet", "max"), (reloaded.Get("Claude").ModelId, reloaded.Get("claude").Effort));
        Assert.Equal(("gpt-5", (string?)null), (reloaded.Get("codex").ModelId, reloaded.Get("codex").Effort));
        Assert.True(File.Exists(Path.Combine(_tempDir, "chat-agent-preferences.json")));
    }

    [Fact]
    public void SetEffort_KeepsTheModelChosenEarlier()
    {
        var preferences = Create();

        preferences.SetModel("claude", "opus");
        preferences.SetEffort("claude", "high");

        Assert.Equal(new ChatAgentPreference("opus", "high"), preferences.Get("claude"));
    }
}
