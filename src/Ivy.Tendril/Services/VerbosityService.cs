using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public enum VerbosityLevel
{
    Quiet,
    Normal,
    Verbose
}

public interface IVerbosityService
{
    VerbosityLevel Level { get; }
    bool IsVerbose { get; }
    bool IsQuiet { get; }
    bool ShouldLog(LogLevel logLevel);
}

public class VerbosityService : IVerbosityService
{
    public VerbosityLevel Level { get; }

    public VerbosityService()
    {
        var verbose = Environment.GetEnvironmentVariable("TENDRIL_VERBOSE") == "1";
        var quiet = Environment.GetEnvironmentVariable("TENDRIL_QUIET") == "1";

        Level = verbose ? VerbosityLevel.Verbose :
                quiet ? VerbosityLevel.Quiet :
                VerbosityLevel.Normal;
    }

    public bool IsVerbose => Level == VerbosityLevel.Verbose;
    public bool IsQuiet => Level == VerbosityLevel.Quiet;

    public bool ShouldLog(LogLevel logLevel)
    {
        return Level switch
        {
            VerbosityLevel.Quiet => logLevel >= LogLevel.Warning,
            VerbosityLevel.Normal => logLevel >= LogLevel.Information,
            VerbosityLevel.Verbose => true,
            _ => true
        };
    }
}
