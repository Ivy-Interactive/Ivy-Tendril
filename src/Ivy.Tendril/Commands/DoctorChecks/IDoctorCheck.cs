namespace Ivy.Tendril.Commands.DoctorChecks;

internal interface IDoctorCheck
{
    string Name { get; }
    Task<CheckResult> RunAsync();
}

internal record CheckResult(bool HasErrors, List<CheckStatus> Statuses);
internal record CheckStatus(string Label, string Value, StatusKind Kind);

internal enum StatusKind { Ok, Warn, Error }
