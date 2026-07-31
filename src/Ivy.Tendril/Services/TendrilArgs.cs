namespace Ivy.Tendril.Services;

public class TendrilArgs
{
    public bool Beta { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_BETA") == "1";
    public bool Verbose { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_VERBOSE") == "1";
    public bool Quiet { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_QUIET") == "1";
    public string? ApiServer { get; init; } = Environment.GetEnvironmentVariable("TENDRIL_API_SERVER");

    public string ServicesUrl => ApiServer ?? "https://tendril-api.ivy.app";

    public string ServicesWsUrl
    {
        get
        {
            var url = ServicesUrl;
            if (url.StartsWith("https://"))
                return "wss://" + url[8..];
            if (url.StartsWith("http://"))
                return "ws://" + url[7..];
            return url;
        }
    }
}
