using Ivy.Tendril.Services;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class EnvironmentCheck : IDoctorCheck
{
    public string Name => "Environment";

    public async Task<CheckResult> RunAsync()
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        // Check TENDRIL_HOME
        var tendrilHome = Environment.GetEnvironmentVariable("TENDRIL_HOME")?.Trim();
        if (!string.IsNullOrEmpty(tendrilHome) && tendrilHome.StartsWith("\"") && tendrilHome.EndsWith("\""))
            tendrilHome = tendrilHome[1..^1];

        if (string.IsNullOrEmpty(tendrilHome))
        {
            statuses.Add(new CheckStatus("TENDRIL_HOME", "Not set", StatusKind.Error));
            return new CheckResult(true, statuses);
        }

        statuses.Add(new CheckStatus("TENDRIL_HOME", tendrilHome, StatusKind.Ok));

        // Check config.yaml
        var configPath = Path.Combine(tendrilHome, "config.yaml");
        if (File.Exists(configPath))
        {
            statuses.Add(new CheckStatus("config.yaml", configPath, StatusKind.Ok));
        }
        else
        {
            statuses.Add(new CheckStatus("config.yaml", $"Not found at {configPath}", StatusKind.Error));
            hasErrors = true;
        }

        // Try to load config
        try
        {
            var configService = new ConfigService();
            if (configService.NeedsOnboarding)
            {
                statuses.Add(new CheckStatus("Config", "Needs onboarding — config could not be loaded", StatusKind.Warn));
                hasErrors = true;
            }
        }
        catch (Exception ex)
        {
            statuses.Add(new CheckStatus("Config", $"Failed to load: {ex.Message}", StatusKind.Error));
            hasErrors = true;
        }

        return await Task.FromResult(new CheckResult(hasErrors, statuses));
    }
}
