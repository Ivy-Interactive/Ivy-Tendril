using System.Reflection;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test.Services;

public class PlanYamlRepairKeyCoverageTests
{
    [Fact]
    public void TopLevelKeys_CoversEveryPlanYamlProperty()
    {
        var field = typeof(PlanYamlRepairService).GetField("TopLevelKeys", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var topLevelKeys = (HashSet<string>)field!.GetValue(null)!;

        foreach (var property in typeof(PlanYaml).GetProperties())
        {
            var camelCaseName = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            Assert.True(topLevelKeys.Contains(camelCaseName),
                $"TopLevelKeys is missing '{camelCaseName}' (property {property.Name})");
        }
    }

    [Fact]
    public void RepairPlanYaml_PreservesChatSessionIdAndPartialDelivery()
    {
        var yaml = "title: Plan\nstate: Draft\nchatSessionId: sess-123\npartialDelivery: true\n";

        var repaired = PlanYamlRepairService.RepairPlanYaml(yaml);

        Assert.Contains("chatSessionId: sess-123", repaired);
        Assert.Contains("partialDelivery: true", repaired);
    }
}
