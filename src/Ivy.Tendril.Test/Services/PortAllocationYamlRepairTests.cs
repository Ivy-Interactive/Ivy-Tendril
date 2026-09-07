using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Test.Services;

/// <summary>
///     The repair pass rewrites plan.yaml before it is deserialized, so it has to recognise
///     <c>allocatedPorts</c> as a mapping. Without that, a port named after a plan field - or an
///     indentation slip - loses the whole allocation and every worktree is handed a new port.
/// </summary>
public class PortAllocationYamlRepairTests
{
    private const string Preamble =
        "schemaVersion: 1\n" +
        "state: Executing\n" +
        "project: Widgets\n" +
        "title: Test\n";

    private static PlanYaml Repair(string yaml) =>
        YamlHelper.Deserializer.Deserialize<PlanYaml>(PlanYamlRepairService.RepairPlanYaml(yaml));

    [Fact]
    public void RepairPlanYaml_WellFormedAllocatedPorts_SurvivesRoundTrip()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "  backend: 31234\n" +
                          "  frontend: 31235\n");

        Assert.Equal(31234, plan.AllocatedPorts!["backend"]);
        Assert.Equal(31235, plan.AllocatedPorts["frontend"]);
    }

    [Fact]
    public void RepairPlanYaml_PortNamedAfterAPlanField_DoesNotClobberThatField()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "  title: 31234\n");

        // A port sharing a top-level key's name is dropped rather than hoisted: the mapping has to
        // terminate at the next real key, and losing the plan's title would be far worse.
        Assert.Equal("Test", plan.Title);
        Assert.True(plan.AllocatedPorts is null || !plan.AllocatedPorts.ContainsKey("title"));
    }

    [Fact]
    public void RepairPlanYaml_ScalarKeyFollowingAllocatedPorts_TerminatesTheMapping()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "  backend: 31234\n" +
                          "level: Feature\n");

        Assert.Equal(31234, plan.AllocatedPorts!["backend"]);
        Assert.Equal("Feature", plan.Level);
    }

    [Fact]
    public void RepairPlanYaml_UnderIndentedPortEntry_IsStillReadAsAPort()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "backend: 31234\n");

        Assert.Equal(31234, plan.AllocatedPorts!["backend"]);
    }

    [Fact]
    public void RepairPlanYaml_HyphenatedPortName_IsPreserved()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "  web-ui: 31235\n");

        Assert.Equal(31235, plan.AllocatedPorts!["web-ui"]);
    }

    [Fact]
    public void RepairPlanYaml_KeyAfterAllocatedPorts_StaysTopLevel()
    {
        var plan = Repair(Preamble +
                          "allocatedPorts:\n" +
                          "  backend: 31234\n" +
                          "repos:\n" +
                          "  - /repos/widgets\n");

        Assert.Equal(31234, plan.AllocatedPorts!["backend"]);
        Assert.Equal(["/repos/widgets"], plan.Repos);
    }

    [Fact]
    public void RepairPlanYaml_NoAllocatedPorts_LeavesItNull()
    {
        var plan = Repair(Preamble + "repos:\n  - /repos/widgets\n");

        Assert.Null(plan.AllocatedPorts);
    }
}
