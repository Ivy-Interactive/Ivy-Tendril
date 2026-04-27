using Microsoft.Extensions.Configuration;

namespace Ivy.Tendril.Test.End2End.Configuration;

public static class TestSettingsProvider
{
    private static E2ETestSettings? _cached;

    public static E2ETestSettings Get()
    {
        if (_cached != null) return _cached;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.e2e.json", optional: true)
            .AddEnvironmentVariables(prefix: "E2E__")
            .Build();

        var settings = new E2ETestSettings();
        config.GetSection("E2E").Bind(settings);

        // Environment variables without the E2E section prefix also bind directly
        config.Bind(settings);

        // Resolve TendrilProjectPath: default to sibling project relative to source directory
        if (string.IsNullOrEmpty(settings.TendrilProjectPath))
        {
            // Walk up from bin/Debug/net10.0/ to find the src directory
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Ivy.Tendril.Test.End2End.csproj")))
                dir = Path.GetDirectoryName(dir);

            if (dir != null)
                settings.TendrilProjectPath = Path.GetFullPath(
                    Path.Combine(dir, "..", "Ivy.Tendril", "Ivy.Tendril.csproj"));
        }
        else if (!Path.IsPathRooted(settings.TendrilProjectPath))
        {
            settings.TendrilProjectPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, settings.TendrilProjectPath));
        }

        _cached = settings;
        return settings;
    }

    public static void Reset() => _cached = null;
}
