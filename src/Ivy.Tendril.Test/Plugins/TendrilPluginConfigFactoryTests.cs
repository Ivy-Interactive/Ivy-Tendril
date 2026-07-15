using Ivy.Tendril.Plugins;

namespace Ivy.Tendril.Test.Plugins;

public class TendrilPluginConfigFactoryTests : IDisposable
{
    private readonly string _tempHome = Path.Combine(Path.GetTempPath(), "tendril-plugin-config-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
            Directory.Delete(_tempHome, recursive: true);
    }

    [Fact]
    public void SetValue_Save_PersistsAndReloads()
    {
        var factory = new TendrilPluginConfigFactory(_tempHome);
        var config = factory.Create("my-plugin");
        config.SetValue("BotToken", "xoxb-123");
        config.SetValue("Channel", "C042");
        config.Save();

        var reloaded = factory.Create("my-plugin");
        Assert.Equal("xoxb-123", reloaded.GetValue("BotToken"));
        Assert.Equal("C042", reloaded.GetValue("Channel"));
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsNull()
    {
        var config = new TendrilPluginConfigFactory(_tempHome).Create("my-plugin");
        Assert.Null(config.GetValue("Nope"));
    }

    [Fact]
    public void RemoveValue_RemovesPersistedKey()
    {
        var factory = new TendrilPluginConfigFactory(_tempHome);
        var config = factory.Create("my-plugin");
        config.SetValue("Key", "value");
        config.Save();

        config.RemoveValue("Key");
        config.Save();

        Assert.Null(factory.Create("my-plugin").GetValue("Key"));
    }

    [Fact]
    public void Create_CorruptConfigFile_ReturnsEmptyConfig()
    {
        var factory = new TendrilPluginConfigFactory(_tempHome);
        Directory.CreateDirectory(factory.ConfigDirectory);
        File.WriteAllText(Path.Combine(factory.ConfigDirectory, "my-plugin.json"), "{not json");

        Assert.Null(factory.Create("my-plugin").GetValue("Key"));
    }

    [Fact]
    public void Create_PluginIdWithPathSeparators_SanitizesFileName()
    {
        var factory = new TendrilPluginConfigFactory(_tempHome);
        var config = factory.Create("weird/../plugin");
        config.SetValue("Key", "value");
        config.Save();

        var files = Directory.GetFiles(factory.ConfigDirectory);
        Assert.Single(files);
        Assert.Equal("value", factory.Create("weird/../plugin").GetValue("Key"));
    }
}
