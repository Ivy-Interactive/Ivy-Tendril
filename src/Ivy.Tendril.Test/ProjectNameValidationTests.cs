using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test;

public class ProjectNameValidationTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectNameValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"project-name-validation-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ValidateProjectNames_Logs_Error_For_Slashed_Name()
    {
        var yaml = @"
projects:
  - name: foo/bar
    repos: []
";

        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.yaml"), yaml);

        var testLogger = new TestLogger<ConfigService>();
        // Use the in-memory-settings ctor (not the public one) so construction doesn't read/write
        // the shared default TENDRIL_HOME — that's a parallel-test race. SetTendrilHome below loads
        // the real config from the isolated temp dir.
        var service = new ConfigService(new TendrilSettings(), logger: testLogger);
        service.SetTendrilHome(configDir);

        service.ValidateProjectNames();

        var output = testLogger.GetOutput();
        Assert.Contains("CRITICAL", output);
        Assert.Contains("foo/bar", output);
    }

    [Fact]
    public void ValidateProjectNames_NoError_For_Valid_Name()
    {
        var yaml = @"
projects:
  - name: Ivy-Tendril
    repos: []
";

        var configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.yaml"), yaml);

        var testLogger = new TestLogger<ConfigService>();
        var service = new ConfigService(new TendrilSettings(), logger: testLogger);
        service.SetTendrilHome(configDir);

        service.ValidateProjectNames();

        var output = testLogger.GetOutput();
        Assert.DoesNotContain("CRITICAL", output);
    }

    private class TestLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public string GetOutput() => string.Join(Environment.NewLine, _messages);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
