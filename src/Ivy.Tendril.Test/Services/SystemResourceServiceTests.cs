using System;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.SystemResources;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class SystemResourceServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsPopulatedSnapshot()
    {
        using var service = new SystemResourceService();

        var snapshot = await service.GetSnapshotAsync();

        Assert.NotNull(snapshot);
        Assert.True(snapshot.Timestamp <= DateTime.UtcNow);

        // CPU Checks
        Assert.NotNull(snapshot.Cpu);
        Assert.True(snapshot.Cpu.CoreCount > 0);
        Assert.InRange(snapshot.Cpu.UsagePercent, 0.0, 100.0);

        // Memory Checks
        Assert.NotNull(snapshot.Memory);
        Assert.True(snapshot.Memory.TotalPhysicalBytes > 0);
        Assert.InRange(snapshot.Memory.UsagePercent, 0.0, 100.0);

        // Disk Checks
        Assert.NotNull(snapshot.Disks);
        foreach (var disk in snapshot.Disks)
        {
            Assert.False(string.IsNullOrWhiteSpace(disk.Name));
            Assert.True(disk.TotalBytes > 0);
            Assert.InRange(disk.UsagePercent, 0.0, 100.0);
        }

        // GPU Checks
        Assert.NotNull(snapshot.Gpu);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Gpu.DeviceName));

        // Process Checks
        Assert.True(snapshot.ProcessMemoryBytes > 0);
        Assert.InRange(snapshot.ProcessCpuPercent, 0.0, 100.0);
    }

    [Fact]
    public void GetHistory_RecordsSnapshotsAndMaintainsCapacity()
    {
        using var service = new SystemResourceService();

        var history = service.GetHistory();

        Assert.NotNull(history);
        Assert.NotEmpty(history);
        Assert.True(history.Count <= 60);

        foreach (var point in history)
        {
            Assert.InRange(point.CpuPercent, 0.0, 100.0);
            Assert.InRange(point.MemoryPercent, 0.0, 100.0);
        }
    }

    [Fact]
    public async Task Snapshots_EmitsValidSnapshots()
    {
        using var service = new SystemResourceService();

        var tcs = new TaskCompletionSource<SystemResourceSnapshot>();
        using var subscription = service.Snapshots.Subscribe(s => tcs.TrySetResult(s));

        var snapshot = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, snapshot);

        var result = await tcs.Task;
        Assert.NotNull(result);
        Assert.True(result.Cpu.CoreCount > 0);
    }
}
