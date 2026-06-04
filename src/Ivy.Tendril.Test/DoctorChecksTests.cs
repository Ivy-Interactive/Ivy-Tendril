using Ivy.Tendril.Commands.DoctorChecks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class DoctorChecksTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-doctor-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public async Task EnvironmentCheck_MissingConfigFile_ShowsFullPath()
    {
        var tempDir = _tempDir.Path;
        var expectedConfigPath = Path.Combine(tempDir, "config.yaml");

        // Ensure no config file exists
        if (File.Exists(expectedConfigPath))
            File.Delete(expectedConfigPath);

        Environment.SetEnvironmentVariable("TENDRIL_HOME", tempDir);

        try
        {
            var check = new EnvironmentCheck();
            var result = await check.RunAsync();

            var configStatus = result.Statuses.FirstOrDefault(s => s.Label == "config.yaml");
            Assert.NotNull(configStatus);
            Assert.Equal(StatusKind.Error, configStatus.Kind);
            Assert.Contains("Not found at", configStatus.Value);
            Assert.Contains(expectedConfigPath, configStatus.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
        }
    }

    [Fact]
    public void PrintStatus_WithBracketCharacters_DoesNotThrow()
    {
        // Arrange
        var status = new Status(StatusKind.Ok, "Test Label", "[flags] and [error] markup");
        var testConsole = new Spectre.Console.Testing.TestConsole();
        AnsiConsole.Console = testConsole;

        // Act & Assert - should not throw on markup characters
        var exception = Record.Exception(() =>
            Commands.DoctorCommand.PrintStatus(status));

        Assert.Null(exception);

        // Verify the output contains escaped brackets
        var output = testConsole.Output;
        Assert.Contains("[[flags]]", output);
        Assert.Contains("[[error]]", output);
    }
}
