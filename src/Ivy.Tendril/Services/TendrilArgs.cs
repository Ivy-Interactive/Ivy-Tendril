namespace Ivy.Tendril.Services;

public class TendrilArgs
{
    private readonly bool? _beta;
    public bool Beta
    {
        get => _beta ?? (Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1" || Environment.GetEnvironmentVariable("IVY_BETA") == "1");
        init => _beta = value;
    }
    public bool Verbose { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_VERBOSE") == "1";
    public bool Quiet { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_QUIET") == "1";
}
