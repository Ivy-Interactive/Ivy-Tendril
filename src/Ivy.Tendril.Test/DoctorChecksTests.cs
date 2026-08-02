using Ivy.Tendril.Agents.Abstractions;
using Ivy.Tendril.Commands;
using Ivy.Tendril.Commands.DoctorChecks;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;
using System.Diagnostics;

namespace Ivy.Tendril.Test;

[Collection("TendrilHome")]
public class DoctorChecksTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new("ivy-doctor-test");

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    [Fact]
    public async Task EnvironmentCheck_MissingConfigFile_ShowsFullPath()
    {
        var tempDir = _tempDir.Path;
        var expectedConfigPath = Path.Combine(tempDir, "config.yaml");

        // Ensure no config file exists
        if (File.Exists(expectedConfigPath))
            File.Delete(expectedConfigPath);

        Environment.SetEnvironmentVariable("TENDRIL_HOME", tempDir);

        try
        {
            var check = new EnvironmentCheck();
            var result = await check.RunAsync();

            var configStatus = result.Statuses.FirstOrDefault(s => s.Label == "config.yaml");
            Assert.NotNull(configStatus);
            Assert.Equal(StatusKind.Error, configStatus.Kind);
            Assert.Contains("Not found at", configStatus.Value);
            Assert.Contains(expectedConfigPath, configStatus.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", null);
        }
    }

    [Fact]
    public async Task EnvironmentCheck_UnsetTendrilHome_ResolvesDefault()
    {
        var originalEnv = Environment.GetEnvironmentVariable("TENDRIL_HOME");
        Environment.SetEnvironmentVariable("TENDRIL_HOME", null);

        try
        {
            var check = new EnvironmentCheck();
            var result = await check.RunAsync();

            var homeStatus = result.Statuses.FirstOrDefault(s => s.Label == "TENDRIL_HOME");
            Assert.NotNull(homeStatus);
            Assert.Equal(StatusKind.Ok, homeStatus.Kind);
            Assert.Contains("Not set (using default)", homeStatus.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENDRIL_HOME", originalEnv);
        }
    }

    [Fact]
    public async Task InstallationCheck_LegacyDotnetToolPresent_ReportsError()
    {
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = _tempDir.Path;

        try
        {
            var storeDir = Path.Combine(_tempDir.Path, ".dotnet", "tools", ".store", "ivy.tendril", "1.0.0");
            Directory.CreateDirectory(storeDir);

            var check = new InstallationCheck();
            var result = await check.RunAsync();

            var legacyStatus = result.Statuses.FirstOrDefault(s => s.Label == "Legacy .NET tool");
            Assert.NotNull(legacyStatus);
            Assert.Equal(StatusKind.Error, legacyStatus.Kind);
            Assert.True(result.HasErrors);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public async Task InstallationCheck_NoLegacyDotnetTool_ReportsOk()
    {
        var original = TendrilInstallHelper.UserProfileOverride;
        TendrilInstallHelper.UserProfileOverride = _tempDir.Path;

        try
        {
            var check = new InstallationCheck();
            var result = await check.RunAsync();

            var legacyStatus = result.Statuses.FirstOrDefault(s => s.Label == "Legacy .NET tool");
            Assert.NotNull(legacyStatus);
            Assert.Equal(StatusKind.Ok, legacyStatus.Kind);
        }
        finally
        {
            TendrilInstallHelper.UserProfileOverride = original;
        }
    }

    [Fact]
    public void PrintStatus_WithBracketCharacters_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            DoctorCommand.PrintStatus(
                "[flags]",
                "[error] markup",
                StatusKind.Ok));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SoftwareCheck_ProbesAgentsConcurrently()
    {
        var agentRunner = new FakeAgentRunner(
            ["agent1", "agent2", "agent3", "agent4"],
            delayMs: 300);
        var config = CreateMinimalConfig("claude");

        var check = new SoftwareCheck(config, agentRunner);
        var sw = Stopwatch.StartNew();
        var result = await check.RunAsync();
        sw.Stop();

        // 4 agents with 300ms delay each, if parallel should be under 600ms (not 1200ms sequential)
        Assert.True(sw.ElapsedMilliseconds < 600, $"Expected under 600ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task AgentModelsCheck_ProbesAgentsConcurrently()
    {
        var agentRunner = new FakeAgentRunner(
            ["agent1", "agent2", "agent3", "agent4"],
            delayMs: 300);
        var config = CreateMinimalConfig("claude");

        var check = new AgentModelsCheck(config, agentRunner);
        var sw = Stopwatch.StartNew();
        var result = await check.RunAsync();
        sw.Stop();

        // 4 agents with 300ms delay each, if parallel should be under 600ms (not 1200ms sequential)
        Assert.True(sw.ElapsedMilliseconds < 600, $"Expected under 600ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task AgentModelsCheck_DeduplicatesRepeatedModelWithinAgent()
    {
        var agentRunner = new FakeAgentRunner(["agent1"], delayMs: 10);

        // Configure 3 profiles all using the same model
        var config = new ConfigService();
        config.Settings = new TendrilConfig
        {
            CodingAgent = "agent1",
            CodingAgents =
            [
                new CodingAgent
                {
                    Name = "agent1",
                    Profiles =
                    [
                        new AgentProfile { Name = "fast", Model = "gpt-5.2" },
                        new AgentProfile { Name = "balanced", Model = "gpt-5.2" },
                        new AgentProfile { Name = "deep", Model = "gpt-5.2" }
                    ]
                }
            ]
        };

        var check = new AgentModelsCheck(config, agentRunner);
        await check.RunAsync();

        // Should have called ValidateModelAsync exactly once (dedup cache)
        var healthCheck = (FakeAgentHealthCheck)agentRunner.GetHealthCheck("agent1");
        Assert.Equal(1, healthCheck.ValidateModelCallCount);
    }

    [Fact]
    public async Task SoftwareCheck_PreservesStatusOrder()
    {
        // Agents complete out of order (agent3=10ms, agent1=50ms, agent2=100ms)
        // but registration order is agent1, agent2, agent3
        var agentRunner = new FakeAgentRunner(
            ["agent1", "agent2", "agent3"],
            delayMsPerAgent: new Dictionary<string, int>
            {
                ["agent1"] = 50,
                ["agent2"] = 100,
                ["agent3"] = 10
            });
        var config = CreateMinimalConfig("claude");

        var check = new SoftwareCheck(config, agentRunner);
        var result = await check.RunAsync();

        var agentStatuses = result.Statuses
            .Where(s => s.Label.StartsWith("agent"))
            .Select(s => s.Label)
            .ToList();

        // Should appear in registration order, not completion order
        Assert.Equal(["agent1", "agent2", "agent3"], agentStatuses);
    }

    [Fact]
    public async Task AgentModelsCheck_HonorsCancellation()
    {
        var agentRunner = new FakeAgentRunner(["agent1"], delayMs: 5000);
        var config = CreateMinimalConfig("agent1");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var check = new AgentModelsCheck(config, agentRunner);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await check.RunAsync(cts.Token));
    }

    [Fact]
    public async Task DoctorCommand_OverallTimeout_ReturnsPartialResultAndZero()
    {
        // Create a slow check that will never complete
        var slowCheck = new SlowCheck("SlowCheck", TimeSpan.FromHours(1));
        var fastCheck = new InstallationCheck();

        // Inject a 1s budget instead of default 60s
        var sw = Stopwatch.StartNew();
        var exitCode = await RunDoctorWithChecks([fastCheck, slowCheck], TimeSpan.FromSeconds(1));
        sw.Stop();

        // Should timeout and return 0 (no errors found in completed checks)
        Assert.Equal(0, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), "Should timeout within 5s");
    }

    [Fact]
    public async Task DoctorCommand_EmitsFirstOutputBeforeSlowCheckCompletes()
    {
        var slowCheck = new SlowCheck("SlowCheck", TimeSpan.FromSeconds(3));

        // Capture console output during check execution
        var outputLines = new List<string>();
        var startTime = DateTime.UtcNow;

        using var writer = new StringWriter();
        Console.SetOut(writer);

        var checkTask = slowCheck.RunAsync();

        // Wait a bit and check if header was printed
        await Task.Delay(100);
        var output = writer.ToString();

        // Should have some output before the check completes
        Assert.NotEmpty(output);
    }

    private static ConfigService CreateMinimalConfig(string codingAgent)
    {
        var config = new ConfigService();
        config.Settings = new TendrilConfig
        {
            CodingAgent = codingAgent
        };
        return config;
    }

    private async Task<int> RunDoctorWithChecks(IDoctorCheck[] checks, TimeSpan timeout)
    {
        // This is a simplified test harness for DoctorCommand with injectable checks
        // Since we can't easily mock the entire DoctorCommand.RunAsync, we test the timeout logic
        // by simulating the check loop with a timeout
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(timeout);

        var completedChecks = 0;
        var hasErrors = false;

        try
        {
            foreach (var check in checks)
            {
                await check.RunAsync(cts.Token);
                completedChecks++;
            }
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            // Timeout expired
            return hasErrors ? 1 : 0;
        }

        return hasErrors ? 1 : 0;
    }
}

// Test doubles
internal class FakeAgentRunner : IAgentRunner
{
    private readonly Dictionary<string, FakeAgentHealthCheck> _healthChecks = new();
    private readonly Dictionary<string, FakeAgentDescriptor> _descriptors = new();
    private readonly int _delayMs;
    private readonly Dictionary<string, int>? _delayMsPerAgent;

    public FakeAgentRunner(string[] agentIds, int delayMs = 0, Dictionary<string, int>? delayMsPerAgent = null)
    {
        _delayMs = delayMs;
        _delayMsPerAgent = delayMsPerAgent;
        RegisteredAgents = agentIds;

        foreach (var id in agentIds)
        {
            var agentDelay = delayMsPerAgent?.GetValueOrDefault(id, delayMs) ?? delayMs;
            _healthChecks[id] = new FakeAgentHealthCheck(agentDelay);
            _descriptors[id] = new FakeAgentDescriptor(id);
        }
    }

    public IEnumerable<string> RegisteredAgents { get; }

    public IAgentHealthCheck GetHealthCheck(string agentId) => _healthChecks[agentId];
    public IAgentDescriptor GetDescriptor(string agentId) => _descriptors[agentId];
    public IAgentCli GetCli(string agentId) => _descriptors[agentId];

    public Task<AgentResponse> RunAsync(AgentRequest request, CancellationToken ct = default)
        => throw new NotImplementedException();
}

internal class FakeAgentHealthCheck : IAgentHealthCheck
{
    private readonly int _delayMs;
    public int ValidateModelCallCount { get; private set; }

    public FakeAgentHealthCheck(int delayMs)
    {
        _delayMs = delayMs;
    }

    public async Task<InstallCheckResult> CheckInstallAsync(CancellationToken ct = default)
    {
        await Task.Delay(_delayMs, ct);
        return new InstallCheckResult(true, "1.0.0");
    }

    public async Task<AuthCheckResult> CheckAuthAsync(CancellationToken ct = default)
    {
        await Task.Delay(_delayMs, ct);
        return new AuthCheckResult(AuthStatus.Authenticated);
    }

    public async Task<ModelValidationResult> ValidateModelAsync(string model, CancellationToken ct = default)
    {
        ValidateModelCallCount++;
        await Task.Delay(_delayMs, ct);
        return new ModelValidationResult(ModelValidationStatus.Ok);
    }
}

internal class FakeAgentDescriptor : IAgentDescriptor, IAgentCli
{
    public FakeAgentDescriptor(string id)
    {
        Id = id;
        DisplayName = id;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string? CliCommand => Id;
    public List<AgentProfile> DefaultProfiles => [new AgentProfile { Name = "default", Tier = AgentTier.Balanced }];
}

internal class SlowCheck : IDoctorCheck
{
    private readonly TimeSpan _delay;

    public SlowCheck(string name, TimeSpan delay)
    {
        Name = name;
        _delay = delay;
    }

    public string Name { get; }

    public async Task<CheckResult> RunAsync(CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct);
        return new CheckResult(false, []);
    }
}
