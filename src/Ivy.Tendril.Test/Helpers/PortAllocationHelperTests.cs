using System.Net;
using System.Net.Sockets;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Helpers;

public class PortAllocationHelperTests
{
    private static ProjectConfig BuildProject(params (string Name, int DefaultPort)[] ports)
    {
        var project = new ProjectConfig { Name = "Ports" };
        foreach (var (name, defaultPort) in ports)
            project.Ports[name] = new ProjectPortConfig { DefaultPort = defaultPort };
        return project;
    }

    /// <summary>Availability probe that treats the named ports as occupied and everything else as free.</summary>
    private static Func<int, bool> Busy(params int[] busyPorts) => port => !busyPorts.Contains(port);

    [Fact]
    public void ResolvePorts_DefaultPortFree_UsesDefaultPort()
    {
        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), null, new HashSet<int>(), Busy());

        Assert.Equal(3001, resolved["backend"]);
    }

    [Fact]
    public void ResolvePorts_DefaultPortBusy_FallsBackToEphemeralRange()
    {
        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), null, new HashSet<int>(), Busy(3001));

        Assert.Equal(PortAllocationHelper.EphemeralRangeStart, resolved["backend"]);
        Assert.InRange(resolved["backend"],
            PortAllocationHelper.EphemeralRangeStart, PortAllocationHelper.EphemeralRangeEnd);
    }

    [Fact]
    public void ResolvePorts_ExistingPortStillFree_IsRetained()
    {
        var existing = new Dictionary<string, int> { ["backend"] = 31234 };

        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), existing, new HashSet<int>(), Busy());

        // The default port is free, but a reviewer may already have the retained URL open.
        Assert.Equal(31234, resolved["backend"]);
    }

    [Fact]
    public void ResolvePorts_ExistingPortNowBusy_IsReallocated()
    {
        var existing = new Dictionary<string, int> { ["backend"] = 31234 };

        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), existing, new HashSet<int>(), Busy(31234));

        Assert.Equal(3001, resolved["backend"]);
    }

    [Fact]
    public void ResolvePorts_TwoNamesSharingADefault_DoNotCollide()
    {
        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3000), ("frontend", 3000)), null, new HashSet<int>(), Busy());

        Assert.Equal(3000, resolved["backend"]);
        Assert.NotEqual(3000, resolved["frontend"]);
    }

    [Fact]
    public void ResolvePorts_PortReservedByAnotherPlan_IsAvoided()
    {
        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), null, new HashSet<int> { 3001 }, Busy());

        Assert.NotEqual(3001, resolved["backend"]);
    }

    [Fact]
    public void ResolvePorts_ReservedExistingPort_IsNotRetained()
    {
        var existing = new Dictionary<string, int> { ["backend"] = 31234 };

        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), existing, new HashSet<int> { 31234 }, Busy());

        Assert.Equal(3001, resolved["backend"]);
    }

    [Fact]
    public void ResolvePorts_RangeExhausted_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            PortAllocationHelper.ResolvePorts(
                BuildProject(("backend", 3001)), null, new HashSet<int>(), _ => false));

        Assert.Contains("No free TCP port available for 'backend'", ex.Message);
    }

    [Fact]
    public void ResolvePorts_NameRemovedFromConfig_KeepsItsRecordedPort()
    {
        var existing = new Dictionary<string, int> { ["retired"] = 31234 };

        var resolved = PortAllocationHelper.ResolvePorts(
            BuildProject(("backend", 3001)), existing, new HashSet<int>(), Busy());

        Assert.Equal(3001, resolved["backend"]);
        Assert.Equal(31234, resolved["retired"]);
    }

    [Fact]
    public void ResolvePorts_NoPortsConfigured_ReturnsEmpty()
    {
        var resolved = PortAllocationHelper.ResolvePorts(
            new ProjectConfig { Name = "None" }, null, new HashSet<int>(), Busy());

        Assert.Empty(resolved);
    }

    [Fact]
    public void IsPortAvailable_BoundPort_ReturnsFalse()
    {
        // Loopback only: binding 0.0.0.0 is blocked on some hosts.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            Assert.False(PortAllocationHelper.IsPortAvailable(port));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void IsPortAvailable_FreePort_ReturnsTrue()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.True(PortAllocationHelper.IsPortAvailable(port));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void IsPortAvailable_OutOfRange_ReturnsFalse(int port)
    {
        Assert.False(PortAllocationHelper.IsPortAvailable(port));
    }
}

[Collection("TendrilHome")]
public class PortAllocationHelperPlanTests : IDisposable
{
    private readonly string _plansDir;
    private readonly TempDirectoryFixture _tempDir = new("tendril-port-alloc-test");

    public PortAllocationHelperPlanTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        Directory.CreateDirectory(_plansDir);
    }

    public void Dispose() => _tempDir.Dispose();

    private string CreatePlan(string id, string title, string state = "Executing", Dictionary<string, int>? allocatedPorts = null)
    {
        var planFolder = Path.Combine(_plansDir, $"{id}-{title}");
        Directory.CreateDirectory(planFolder);

        var plan = new PlanYaml
        {
            State = state,
            Project = "Ports",
            Title = title,
            Repos = [_tempDir.Path],
            AllocatedPorts = allocatedPorts
        };

        File.WriteAllText(Path.Combine(planFolder, "plan.yaml"), YamlHelper.Serializer.Serialize(plan));
        return planFolder;
    }

    private static ProjectConfig BuildProject(params (string Name, int DefaultPort)[] ports)
    {
        var project = new ProjectConfig { Name = "Ports" };
        foreach (var (name, defaultPort) in ports)
            project.Ports[name] = new ProjectPortConfig { DefaultPort = defaultPort };
        return project;
    }

    [Fact]
    public void AllocatePorts_WritesAllocationToPlanYaml()
    {
        var planFolder = CreatePlan("20001", "AllocateWrites");
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        var allocated = PortAllocationHelper.AllocatePorts(BuildProject(("backend", 0)), plan, planFolder);

        Assert.Single(allocated);
        var reloaded = PlanCommandHelpers.ReadPlan(planFolder);
        Assert.Equal(allocated["backend"], reloaded.AllocatedPorts!["backend"]);
    }

    [Fact]
    public void AllocatePorts_SecondCall_RetainsTheSamePorts()
    {
        var planFolder = CreatePlan("20002", "AllocateStable");

        var first = PortAllocationHelper.AllocatePorts(
            BuildProject(("backend", 0)), PlanCommandHelpers.ReadPlan(planFolder), planFolder);
        var second = PortAllocationHelper.AllocatePorts(
            BuildProject(("backend", 0)), PlanCommandHelpers.ReadPlan(planFolder), planFolder);

        Assert.Equal(first["backend"], second["backend"]);
    }

    [Fact]
    public void AllocatePorts_NoPortsConfigured_LeavesPlanUntouched()
    {
        var planFolder = CreatePlan("20003", "NoPorts");
        var before = File.ReadAllText(Path.Combine(planFolder, "plan.yaml"));

        var allocated = PortAllocationHelper.AllocatePorts(
            new ProjectConfig { Name = "Ports" }, PlanCommandHelpers.ReadPlan(planFolder), planFolder);

        Assert.Empty(allocated);
        Assert.Equal(before, File.ReadAllText(Path.Combine(planFolder, "plan.yaml")));
    }

    [Fact]
    public void AllocatePorts_PortHeldByAnotherActivePlan_PicksADifferentOne()
    {
        CreatePlan("20004", "Neighbour", allocatedPorts: new Dictionary<string, int> { ["backend"] = 31500 });
        var planFolder = CreatePlan("20005", "Allocating");

        var allocated = PortAllocationHelper.AllocatePorts(
            BuildProject(("backend", 31500)), PlanCommandHelpers.ReadPlan(planFolder), planFolder);

        Assert.NotEqual(31500, allocated["backend"]);
    }

    [Fact]
    public void CollectReservedPorts_SkipsCompletedPlans()
    {
        CreatePlan("20006", "Done", "Completed", new Dictionary<string, int> { ["backend"] = 31600 });
        CreatePlan("20007", "Running", "Review", new Dictionary<string, int> { ["backend"] = 31601 });

        var reserved = PortAllocationHelper.CollectReservedPorts(_plansDir, null);

        // A completed plan's worktree is gone, so holding its port back would exhaust the range.
        Assert.DoesNotContain(31600, reserved);
        Assert.Contains(31601, reserved);
    }

    [Fact]
    public void CollectReservedPorts_ExcludesTheRequestingPlan()
    {
        var planFolder = CreatePlan("20008", "Self", allocatedPorts: new Dictionary<string, int> { ["backend"] = 31700 });

        var reserved = PortAllocationHelper.CollectReservedPorts(_plansDir, planFolder);

        Assert.DoesNotContain(31700, reserved);
    }
}
