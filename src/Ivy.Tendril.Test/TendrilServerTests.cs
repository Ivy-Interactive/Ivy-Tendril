using System.Reflection;
using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test;

public class TendrilServerTests
{
    [Fact]
    public void AllowedFileExtensions_ContainsRequiredExtensions()
    {
        var expectedExtensions = new[]
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".svg", ".webp", ".ico", ".avif", ".pdf"
        };

        Assert.Equal(expectedExtensions.Length, LocalFileGuardMiddleware.AllowedFileExtensions.Length);

        foreach (var ext in expectedExtensions)
        {
            Assert.Contains(ext, LocalFileGuardMiddleware.AllowedFileExtensions, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TendrilServer_Create_ExecutesSuccessfully()
    {
        var server = TendrilServer.Create([], new TendrilArgs());
        Assert.NotNull(server);
    }

    [Fact]
    public void ConfigureLocalFileAccess_ConfiguresRootsAndExtensions()
    {
        var server = new Server();
        var configService = new ConfigService(NullLogger<ConfigService>.Instance);

        TendrilServer.ConfigureLocalFileAccess(server, configService);

        var rootsProp = server.Args.GetType().GetProperty("LocalFileRoots");
        var extProp = server.Args.GetType().GetProperty("LocalFileExtensions");

        if (rootsProp != null && extProp != null)
        {
            var configuredRoots = (string[])rootsProp.GetValue(server.Args)!;
            var configuredExtensions = (string[])extProp.GetValue(server.Args)!;

            var expectedRoots = LocalFileRootPolicy.ComputeRoots(configService);
            if (expectedRoots.Count == 0 && !string.IsNullOrWhiteSpace(configService.TendrilHome))
            {
                expectedRoots = [configService.TendrilHome];
            }

            foreach (var expectedRoot in expectedRoots)
            {
                Assert.True(configuredRoots.Any(r =>
                    r.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
                    (LocalFileRootPolicy.TryResolveRealPath(expectedRoot, out var real) && r.Equals(real, StringComparison.OrdinalIgnoreCase))),
                    $"Expected configuredRoots to contain {expectedRoot}, but had: {string.Join(", ", configuredRoots)}");
            }

            Assert.Equal(LocalFileGuardMiddleware.AllowedFileExtensions, configuredExtensions);

            var policyType = typeof(Server).Assembly.GetType("Ivy.LocalFileAccessPolicy");
            var describeMethod = policyType?.GetMethod(
                "DescribeUnconfinedAccess",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            if (describeMethod != null)
            {
                var warning = describeMethod.Invoke(null, [server.Args]);
                Assert.Null(warning);
            }
        }
        else
        {
            // Ivy 1.4.0 fallback: parameterless DangerouslyAllowLocalFiles was invoked
            Assert.True(server.Args.DangerouslyAllowLocalFiles);
        }
    }

    [Fact]
    public void SettingsReloaded_RefreshesRoots_WhenSupported()
    {
        var server = new Server();
        var tempHome = Path.Combine(Path.GetTempPath(), "tendril_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempHome);

        try
        {
            var configService = new ConfigService(new TendrilSettings(), tempHome);
            TendrilServer.ConfigureLocalFileAccess(server, configService);

            var rootsProp = server.Args.GetType().GetProperty("LocalFileRoots");
            if (rootsProp != null)
            {
                var initialRoots = (string[])rootsProp.GetValue(server.Args)!;
                Assert.True(initialRoots.Any(r =>
                    r.Equals(tempHome, StringComparison.OrdinalIgnoreCase) ||
                    (LocalFileRootPolicy.TryResolveRealPath(tempHome, out var realHome) && r.Equals(realHome, StringComparison.OrdinalIgnoreCase))),
                    $"Expected initialRoots to contain {tempHome}, but had: {string.Join(", ", initialRoots)}");

                var newRoot = Path.Combine(Path.GetTempPath(), "extra_root_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(newRoot);
                try
                {
                    configService.Settings.Security ??= new SecuritySettings();
                    configService.Settings.Security.LocalFileRoots = [newRoot];
                    configService.SaveSettings();

                    var refreshedRoots = (string[])rootsProp.GetValue(server.Args)!;
                    Assert.True(refreshedRoots.Any(r =>
                        r.Equals(newRoot, StringComparison.OrdinalIgnoreCase) ||
                        (LocalFileRootPolicy.TryResolveRealPath(newRoot, out var realNew) && r.Equals(realNew, StringComparison.OrdinalIgnoreCase))),
                        $"Expected refreshedRoots to contain {newRoot}, but had: {string.Join(", ", refreshedRoots)}");
                }
                finally
                {
                    if (Directory.Exists(newRoot))
                    {
                        Directory.Delete(newRoot, true);
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempHome))
            {
                Directory.Delete(tempHome, true);
            }
        }
    }
}
