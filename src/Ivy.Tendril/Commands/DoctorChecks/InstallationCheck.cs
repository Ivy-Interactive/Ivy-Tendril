using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Commands.DoctorChecks;

internal class InstallationCheck : IDoctorCheck
{
    public string Name => "Installation";

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        var statuses = new List<CheckStatus>();
        var hasErrors = false;

        var version = typeof(Program).Assembly.GetName().Version?.ToString(3);
        statuses.Add(new CheckStatus("Version", version ?? "Unknown", StatusKind.Ok));

        var executable = Environment.ProcessPath;
        statuses.Add(new CheckStatus("Executable", executable ?? "Unknown", StatusKind.Ok));

        var onPath = TendrilInstallHelper.ResolveOnPath("tendril");
        if (onPath == null)
        {
            statuses.Add(new CheckStatus("tendril on PATH", "Not found on PATH", StatusKind.Warn));
        }
        else
        {
            var installedCli = TendrilInstallHelper.FindInstalledCli();
            var resolvesToKnownGoodCli =
                (executable != null && string.Equals(onPath, executable, StringComparison.OrdinalIgnoreCase)) ||
                (installedCli != null && string.Equals(onPath, installedCli, StringComparison.OrdinalIgnoreCase));
            statuses.Add(new CheckStatus("tendril on PATH", onPath, resolvesToKnownGoodCli ? StatusKind.Ok : StatusKind.Warn));
        }

        if (TendrilInstallHelper.IsLegacyToolInstalled())
        {
            var legacyVersion = TendrilInstallHelper.GetLegacyToolVersion();
            var legacyPath = TendrilInstallHelper.FindLegacyToolInstallPath();
            statuses.Add(new CheckStatus(
                "Legacy .NET tool",
                $"Conflicting global tool v{legacyVersion ?? "unknown"} at {legacyPath}. Run: dotnet tool uninstall --global Ivy.Tendril",
                StatusKind.Error));
            hasErrors = true;
        }
        else
        {
            statuses.Add(new CheckStatus("Legacy .NET tool", "Not installed", StatusKind.Ok));
        }

        return await Task.FromResult(new CheckResult(hasErrors, statuses));
    }
}
