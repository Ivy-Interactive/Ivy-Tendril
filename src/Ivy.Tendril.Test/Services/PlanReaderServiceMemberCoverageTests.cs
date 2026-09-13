using System.Reflection;
using Ivy.Tendril.Services.Plans;

namespace Ivy.Tendril.Test.Services;

public class PlanReaderServiceMemberCoverageTests
{
    [Fact]
    public void PlanReaderService_ImplementsAllIPlanReaderServiceMembersDirectly()
    {
        var map = typeof(PlanReaderService).GetInterfaceMap(typeof(IPlanReaderService));
        var missingImplementations = new List<string>();

        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            var interfaceMethod = map.InterfaceMethods[i];
            var targetMethod = map.TargetMethods[i];

            if (targetMethod.DeclaringType != typeof(PlanReaderService))
            {
                missingImplementations.Add($"{interfaceMethod.DeclaringType?.Name}.{interfaceMethod.Name} ({interfaceMethod}) -> Target DeclaringType: {targetMethod.DeclaringType?.Name}");
            }
        }

        Assert.True(
            missingImplementations.Count == 0,
            $"PlanReaderService must directly implement all IPlanReaderService members to avoid inheriting default interface method stubs. Missing implementations:\n{string.Join("\n", missingImplementations)}");
    }
}
