namespace Ivy.Tendril.Services;

public class TendrilArgs
{
    public bool Beta { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1";
    public bool Verbose { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_VERBOSE") == "1";
    public bool Quiet { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_QUIET") == "1";
}
