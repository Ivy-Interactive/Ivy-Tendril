using System;
using System.IO;
using Ivy.Tendril.Helpers;
using Xunit;

namespace Ivy.Tendril.Test;

public class PromptwareHelperTests : IDisposable
{
    private readonly string? _originalTendrilHome;

    public PromptwareHelperTests()
    {
        _originalTendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_HOME", _originalTendrilHome);
    }

    [Fact]
    public void ResolvePromptsRoot_WithEmptyTendrilHome_FallsBackToEnvironmentVariable()
    {
        // Arrange
        var tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var tempPromptwares = Path.Combine(tempHome, "Promptwares");
        Directory.CreateDirectory(tempPromptwares);
        Environment.SetEnvironmentVariable("TENDRIL_HOME", tempHome);

        try
        {
            // Act
            var result = PromptwareHelper.ResolvePromptsRoot("");

            // Assert
            Assert.Equal(tempPromptwares, result);
        }
        finally
        {
            if (Directory.Exists(tempHome))
            {
                try { Directory.Delete(tempHome, true); } catch { }
            }
        }
    }

    [Fact]
    public void ResolvePromptwareFolder_WithEmptyTendrilHome_FallsBackToEnvironmentVariable()
    {
        // Arrange
        var tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var tempPromptwares = Path.Combine(tempHome, "Promptwares");
        var tempSetupProject = Path.Combine(tempPromptwares, "SetupProject");
        Directory.CreateDirectory(tempSetupProject);
        var programFile = Path.Combine(tempSetupProject, "Program.md");
        File.WriteAllText(programFile, "# Program");

        Environment.SetEnvironmentVariable("TENDRIL_HOME", tempHome);

        try
        {
            // Act
            var result = PromptwareHelper.ResolvePromptwareFolder("SetupProject", "");

            // Assert
            Assert.Equal(tempSetupProject, result);
        }
        finally
        {
            if (Directory.Exists(tempHome))
            {
                try { Directory.Delete(tempHome, true); } catch { }
            }
        }
    }
}
