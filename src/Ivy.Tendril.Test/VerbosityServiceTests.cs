using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Test;

public class VerbosityServiceTests
{
    [Fact]
    public void Default_ShouldBeNormal()
    {
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", null);
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", null);

        var service = new VerbosityService();

        Assert.Equal(VerbosityLevel.Normal, service.Level);
        Assert.False(service.IsVerbose);
        Assert.False(service.IsQuiet);
    }

    [Fact]
    public void WhenVerboseSet_ShouldBeVerbose()
    {
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", "1");
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", null);

        var service = new VerbosityService();

        Assert.Equal(VerbosityLevel.Verbose, service.Level);
        Assert.True(service.IsVerbose);
        Assert.False(service.IsQuiet);

        // Cleanup
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", null);
    }

    [Fact]
    public void WhenQuietSet_ShouldBeQuiet()
    {
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", null);
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", "1");

        var service = new VerbosityService();

        Assert.Equal(VerbosityLevel.Quiet, service.Level);
        Assert.False(service.IsVerbose);
        Assert.True(service.IsQuiet);

        // Cleanup
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", null);
    }

    [Theory]
    [InlineData(VerbosityLevel.Quiet, LogLevel.Debug, false)]
    [InlineData(VerbosityLevel.Quiet, LogLevel.Information, false)]
    [InlineData(VerbosityLevel.Quiet, LogLevel.Warning, true)]
    [InlineData(VerbosityLevel.Quiet, LogLevel.Error, true)]
    [InlineData(VerbosityLevel.Normal, LogLevel.Debug, false)]
    [InlineData(VerbosityLevel.Normal, LogLevel.Information, true)]
    [InlineData(VerbosityLevel.Normal, LogLevel.Warning, true)]
    [InlineData(VerbosityLevel.Normal, LogLevel.Error, true)]
    [InlineData(VerbosityLevel.Verbose, LogLevel.Trace, true)]
    [InlineData(VerbosityLevel.Verbose, LogLevel.Debug, true)]
    [InlineData(VerbosityLevel.Verbose, LogLevel.Information, true)]
    [InlineData(VerbosityLevel.Verbose, LogLevel.Warning, true)]
    [InlineData(VerbosityLevel.Verbose, LogLevel.Error, true)]
    public void ShouldLog_RespectsVerbosityLevel(VerbosityLevel level, LogLevel logLevel, bool expected)
    {
        // Set up environment for the level
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", level == VerbosityLevel.Verbose ? "1" : null);
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", level == VerbosityLevel.Quiet ? "1" : null);

        var service = new VerbosityService();
        var actual = service.ShouldLog(logLevel);

        Assert.Equal(expected, actual);

        // Cleanup
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", null);
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", null);
    }

    [Fact]
    public void VerboseOverridesQuiet()
    {
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", "1");
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", "1");

        var service = new VerbosityService();

        Assert.Equal(VerbosityLevel.Verbose, service.Level);
        Assert.True(service.IsVerbose);

        // Cleanup
        Environment.SetEnvironmentVariable("TENDRIL_VERBOSE", null);
        Environment.SetEnvironmentVariable("TENDRIL_QUIET", null);
    }
}