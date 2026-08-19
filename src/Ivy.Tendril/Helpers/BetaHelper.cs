namespace Ivy.Tendril.Helpers;

using Ivy.Tendril.Services;

public static class BetaHelper
{
    public static bool IsBeta(TendrilArgs? tendrilArgs = null, IConfigService? config = null)
    {
        return (tendrilArgs?.Beta ?? false)
            || (config?.Settings?.Beta ?? false)
            || Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1"
            || Environment.GetEnvironmentVariable("IVY_BETA") == "1";
    }

    public static bool IsBeta(TendrilArgs? tendrilArgs, TendrilSettings? settings)
    {
        return (tendrilArgs?.Beta ?? false)
            || (settings?.Beta ?? false)
            || Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1"
            || Environment.GetEnvironmentVariable("IVY_BETA") == "1";
    }

    public static bool IsBeta(TendrilSettings? settings)
    {
        return (settings?.Beta ?? false)
            || Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1"
            || Environment.GetEnvironmentVariable("IVY_BETA") == "1";
    }
}
