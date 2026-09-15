using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Agents.Providers.Antigravity;

namespace Ivy.Tendril.Agents.Test.Antigravity;

public class AntigravityHealthCheckTests
{
    private readonly AntigravityHealthCheck _healthCheck = new();

    [Fact]
    public void AgentId_IsAntigravity()
    {
        Assert.Equal(AgentId.Antigravity, _healthCheck.AgentId);
    }

    [Fact]
    public void GetOnboardingInfo_ReturnsCompleteInfo()
    {
        var info = _healthCheck.GetOnboardingInfo();

        Assert.Equal("Antigravity", info.DisplayName);
        Assert.NotEmpty(info.InstallCommand);
        Assert.NotNull(info.AuthCommand);
        Assert.NotNull(info.DocsUrl);
    }

    [Fact]
    public async Task CheckInstall_ReturnsResultWithoutThrowing()
    {
        var status = await _healthCheck.CheckInstallAsync();

        Assert.NotNull(status);
        if (status.IsInstalled)
        {
            Assert.NotNull(status.BinaryPath);
        }
        else
        {
            Assert.NotNull(status.Error);
        }
    }

    // A non-default model used to be waved through with "Model validation not supported via
    // Antigravity CLI" and a Warn, which is why doctor reported a healthy provider while every job
    // launched against it died. The probe spawns `agy`, so this runs it only when the CLI is present
    // and asserts on what the outcome may no longer be, not on a quota-dependent success.
    [Fact]
    public async Task ValidateModelAsync_NonDefaultModel_NoLongerReturnsNotSupported()
    {
        var install = await _healthCheck.CheckInstallAsync();
        if (!install.IsInstalled)
            return;

        var result = await _healthCheck.ValidateModelAsync("gemini-3.8-flash");

        Assert.NotNull(result);
        Assert.DoesNotContain("not supported", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MapProbeResult_SuccessfulResultLine_ReturnsOk()
    {
        var stdout = "{\"event\":\"result\",\"result\":{\"status\":\"SUCCESS\",\"response\":\"OK\",\"duration_seconds\":1.2}}";

        var result = AntigravityHealthCheck.MapProbeResult("gemini-3.8-flash", 0, stdout, "");

        Assert.Equal(ModelValidationStatus.Ok, result.Status);
        Assert.Equal("gemini-3.8-flash", result.Model);
    }

    // The exact failure from the incident: the probe now reports it as a rate limit, which
    // AgentModelsCheck renders as an error.
    [Fact]
    public void MapProbeResult_QuotaError_ReturnsRateLimit()
    {
        var stdout = "{\"event\":\"result\",\"result\":{\"status\":\"ERROR\",\"error\":\"API error (attempt 5): RESOURCE_EXHAUSTED (code 429): Resource has been exhausted (e.g. check quota).\",\"response\":\"\"}}";

        var result = AntigravityHealthCheck.MapProbeResult("gemini-3.8-flash", 1, stdout, "");

        Assert.Equal(ModelValidationStatus.RateLimit, result.Status);
        Assert.Contains("RESOURCE_EXHAUSTED", result.ErrorMessage);
    }

    [Fact]
    public void MapProbeResult_AuthError_ReturnsAuthError()
    {
        var result = AntigravityHealthCheck.MapProbeResult("gemini-3.8-flash", 1, "", "You are not logged in.");

        Assert.Equal(ModelValidationStatus.AuthError, result.Status);
        Assert.Contains("not logged in", result.ErrorMessage);
    }

    [Fact]
    public void MapProbeResult_UnknownModel_ReturnsInvalidModel()
    {
        var result = AntigravityHealthCheck.MapProbeResult("gemini-9.9-imaginary", 1, "", "unknown model: gemini-9.9-imaginary");

        Assert.Equal(ModelValidationStatus.InvalidModel, result.Status);
    }

    // HealthCheckRunner reports its own timeout as exit code -1 with "Timed out" on stderr.
    [Fact]
    public void MapProbeResult_RunnerTimeout_ReturnsTimeout()
    {
        var result = AntigravityHealthCheck.MapProbeResult("gemini-3.8-flash", -1, "", "Timed out");

        Assert.Equal(ModelValidationStatus.Timeout, result.Status);
    }

    [Fact]
    public void MapProbeResult_CleanExitWithNoResultLine_ReturnsOk()
    {
        var result = AntigravityHealthCheck.MapProbeResult("default", 0, "OK\n", "");

        Assert.Equal(ModelValidationStatus.Ok, result.Status);
    }

    [Fact]
    public void MapProbeResult_UnrecognizedFailure_ReturnsUnknownWithDetail()
    {
        var result = AntigravityHealthCheck.MapProbeResult("gemini-3.8-flash", 2, "", "segmentation fault");

        Assert.Equal(ModelValidationStatus.Unknown, result.Status);
        Assert.Equal("segmentation fault", result.ErrorMessage);
    }
}
