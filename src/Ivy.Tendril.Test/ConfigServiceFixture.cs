using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class ConfigServiceFixture : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-config-test");

    public ConfigService Service { get; }

    public ConfigServiceFixture()
    {
        var yaml = @"
projects:
  - name: TestProject
    repos:
      - path: D:\Repos\Test
    context: Test context
verifications: []
";
        var configPath = Path.Combine(_tempDir.Path, "config.yaml");
        File.WriteAllText(configPath, yaml);

        Service = new ConfigService(new TendrilSettings());
        Service.SetTendrilHome(_tempDir.Path);
    }

    public void Dispose()
    {
        (Service as IDisposable)?.Dispose();
        _tempDir.Dispose();
    }
}
