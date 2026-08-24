using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Ivy.Tendril.Services.SystemResources;

namespace Ivy.Tendril.Apps.ResourceMonitor;

public record DiskTableRow(
    string Mount,
    string Label,
    string FileSystem,
    string Usage,
    string Free,
    double Percent
);

[App(title: "Resource Monitor", icon: Icons.Cpu, group: ["Apps"], order: Constants.ResourceMonitor, isVisible: false)]
public class ResourceMonitorApp : ViewBase
{
    public override object Build()
    {
        var resourceService = UseService<ISystemResourceService>();
        var jobService = UseService<IJobService>();
        var client = UseService<IClientProvider>();
        var copyToClipboard = UseClipboard();

        var refreshInterval = UseState(2);
        var refreshToken = UseRefreshToken();

        UseInterval(() =>
        {
            refreshToken.Refresh();
        }, TimeSpan.FromSeconds(Math.Max(1, refreshInterval.Value)));

        var snapshotTask = resourceService.GetSnapshotAsync();
        var snapshot = snapshotTask.IsCompleted ? snapshotTask.Result : null;

        if (snapshot == null)
        {
            return Layout.Vertical().AlignContent(Align.Center).Height(Size.Full()).Gap(2)
                   | Text.Muted("Collecting system resource metrics...");
        }

        var runningJobsCount = jobService.GetJobs().Count(j => j.Status == JobStatus.Running);

        // Header section
        var header = Layout.Vertical().Width(Size.Full().Max(Size.Units(240)))
            | (Layout.Horizontal().AlignContent(Align.Center).Gap(3)
                | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                    | Text.H2("Resource Monitor")
                    | new Badge("Live").Variant(BadgeVariant.Success).Small())
                | new Spacer()
                | (Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                    | Text.Muted("Interval:").Small()
                    | new Button("1s")
                        .Variant(refreshInterval.Value == 1 ? ButtonVariant.Primary : ButtonVariant.Outline)
                        .Small()
                        .OnClick(() => refreshInterval.Set(1))
                    | new Button("2s")
                        .Variant(refreshInterval.Value == 2 ? ButtonVariant.Primary : ButtonVariant.Outline)
                        .Small()
                        .OnClick(() => refreshInterval.Set(2))
                    | new Button("5s")
                        .Variant(refreshInterval.Value == 5 ? ButtonVariant.Primary : ButtonVariant.Outline)
                        .Small()
                        .OnClick(() => refreshInterval.Set(5)))
                | new Button("Copy System Snapshot")
                    .Variant(ButtonVariant.Outline)
                    .Icon(Icons.ClipboardCopy)
                    .Small()
                    .OnClick(() =>
                    {
                        var report = BuildSnapshotReport(snapshot, runningJobsCount);
                        copyToClipboard(report);
                        client.Toast("System snapshot copied to clipboard", "Copied");
                    }));

        // Top Row Grid: CPU, Memory, GPU
        var cpuCard = BuildCpuCard(snapshot.Cpu, snapshot.ProcessCpuPercent);
        var memoryCard = BuildMemoryCard(snapshot.Memory);
        var gpuCard = BuildGpuCard(snapshot.Gpu);

        var topRow = Layout.Grid()
            .Columns(1.At(Breakpoint.Mobile)
                .And(Breakpoint.Tablet, 2)
                .And(Breakpoint.Desktop, 3))
            .Gap(3)
            .Width(Size.Full())
            | cpuCard
            | memoryCard
            | gpuCard;

        // Disks Section
        var disksCard = BuildDisksCard(snapshot.Disks);

        // Process Section
        var processCard = BuildProcessCard(snapshot, runningJobsCount);

        var content = Layout.Vertical().Width(Size.Full().Max(Size.Units(240))).Gap(4)
            | topRow
            | disksCard
            | processCard;

        return new HeaderLayout(header, content);
    }

    private static object BuildCpuCard(CpuMetrics cpu, double processCpuPercent)
    {
        var badgeVariant = cpu.UsagePercent >= 90 ? BadgeVariant.Destructive
            : cpu.UsagePercent >= 75 ? BadgeVariant.Warning
            : BadgeVariant.Primary;

        var loadAvgStr = cpu.LoadAverage1m.HasValue
            ? $"{cpu.LoadAverage1m.Value:F2}, {cpu.LoadAverage5m:F2}, {cpu.LoadAverage15m:F2}"
            : "N/A";

        var cardContent = Layout.Vertical().Gap(3)
            | (Layout.Horizontal().AlignContent(Align.Center)
                | Text.H3($"{cpu.UsagePercent:F1}%")
                | new Spacer()
                | new Badge($"{cpu.CoreCount} Cores").Variant(BadgeVariant.Secondary).Small()
                | new Badge($"{cpu.UsagePercent:F1}% Load").Variant(badgeVariant).Small())
            | new Progress((int)Math.Clamp(Math.Round(cpu.UsagePercent), 0, 100))
            | (Layout.Vertical().Gap(1)
                | (Layout.Horizontal()
                    | Text.Muted("Load Average (1m, 5m, 15m):").Small()
                    | new Spacer()
                    | Text.Block(loadAvgStr).Small())
                | (Layout.Horizontal()
                    | Text.Muted("Process CPU Usage:").Small()
                    | new Spacer()
                    | Text.Block($"{processCpuPercent:F1}%").Small()));

        return new Card(cardContent).Header("CPU", icon: Icons.Cpu);
    }

    private static object BuildMemoryCard(MemoryMetrics memory)
    {
        var ramBadgeVariant = memory.UsagePercent >= 90 ? BadgeVariant.Destructive
            : memory.UsagePercent >= 75 ? BadgeVariant.Warning
            : BadgeVariant.Primary;

        var swapText = memory.TotalSwapBytes > 0
            ? $"{FormatHelper.FormatBytes(memory.UsedSwapBytes)} / {FormatHelper.FormatBytes(memory.TotalSwapBytes)} ({memory.SwapUsagePercent:F1}%)"
            : "No swap configured";

        var cardContent = Layout.Vertical().Gap(3)
            | (Layout.Horizontal().AlignContent(Align.Center)
                | Text.H3($"{memory.UsagePercent:F1}%")
                | new Spacer()
                | Text.Muted($"{FormatHelper.FormatBytes(memory.UsedPhysicalBytes)} / {FormatHelper.FormatBytes(memory.TotalPhysicalBytes)}").Small()
                | new Badge($"{memory.UsagePercent:F1}% RAM").Variant(ramBadgeVariant).Small())
            | new Progress((int)Math.Clamp(Math.Round(memory.UsagePercent), 0, 100))
            | (Layout.Vertical().Gap(1)
                | (Layout.Horizontal()
                    | Text.Muted("Free RAM:").Small()
                    | new Spacer()
                    | Text.Block(FormatHelper.FormatBytes(memory.FreePhysicalBytes)).Small())
                | (Layout.Horizontal()
                    | Text.Muted("Swap / Pagefile:").Small()
                    | new Spacer()
                    | Text.Block(swapText).Small()));

        return new Card(cardContent).Header("Memory", icon: Icons.Activity);
    }

    private static object BuildGpuCard(GpuMetrics gpu)
    {
        var statusBadge = gpu.IsDetected
            ? new Badge("Detected").Variant(BadgeVariant.Success).Small()
            : new Badge("Not Detected").Variant(BadgeVariant.Outline).Small();

        var vramStr = gpu.TotalMemoryBytes.HasValue
            ? FormatHelper.FormatBytes(gpu.TotalMemoryBytes.Value)
            : "Shared / System Memory";

        var cardContent = Layout.Vertical().Gap(3)
            | (Layout.Horizontal().AlignContent(Align.Center)
                | Text.H4(gpu.IsDetected ? gpu.DeviceName : "No GPU detected")
                | new Spacer()
                | statusBadge)
            | (Layout.Vertical().Gap(1)
                | (Layout.Horizontal()
                    | Text.Muted("VRAM / Memory:").Small()
                    | new Spacer()
                    | Text.Block(vramStr).Small())
                | (Layout.Horizontal()
                    | Text.Muted("Driver / Info:").Small()
                    | new Spacer()
                    | Text.Block(gpu.DriverVersion ?? (gpu.IsDetected ? "System Hardware" : "N/A")).Small()));

        return new Card(cardContent).Header("GPU", icon: Icons.Monitor);
    }

    private static object BuildDisksCard(List<DiskPartitionMetrics> disks)
    {
        if (disks.Count == 0)
        {
            return new Card(Text.Muted("No disk drives detected.")).Header("Disk Storage", icon: Icons.HardDrive);
        }

        var diskRows = disks.Select(d => new DiskTableRow(
            Mount: d.Name,
            Label: string.IsNullOrWhiteSpace(d.VolumeLabel) ? "-" : d.VolumeLabel,
            FileSystem: string.IsNullOrWhiteSpace(d.FileSystem) ? d.DriveType : $"{d.DriveType} ({d.FileSystem})",
            Usage: $"{FormatHelper.FormatBytes(d.UsedBytes)} / {FormatHelper.FormatBytes(d.TotalBytes)}",
            Free: FormatHelper.FormatBytes(d.FreeBytes),
            Percent: d.UsagePercent
        )).ToList();

        var table = diskRows.ToTable()
            .Width(Size.Full())
            .Header(x => x.Mount, "Mount")
            .Header(x => x.Label, "Volume")
            .Header(x => x.FileSystem, "Type")
            .Header(x => x.Usage, "Used / Total")
            .Header(x => x.Free, "Free")
            .Header(x => x.Percent, "Utilization")
            .Builder(x => x.Percent, f => f.Func((double percent) =>
            {
                var variant = percent >= 90 ? BadgeVariant.Destructive
                    : percent >= 75 ? BadgeVariant.Warning
                    : BadgeVariant.Primary;

                return Layout.Horizontal().AlignContent(Align.Center).Gap(2)
                    | new Progress((int)Math.Clamp(Math.Round(percent), 0, 100)).Width(Size.Px(120))
                    | new Badge($"{percent:F1}%").Variant(variant).Small();
            }));

        return new Card(table).Header("Disk Storage", icon: Icons.HardDrive);
    }

    private static object BuildProcessCard(SystemResourceSnapshot snapshot, int runningJobsCount)
    {
        var details = Layout.Grid()
            .Columns(2.At(Breakpoint.Mobile).And(Breakpoint.Tablet, 4))
            .Gap(3)
            | BuildMetricBlock("Working Set", FormatHelper.FormatBytes(snapshot.ProcessMemoryBytes))
            | BuildMetricBlock("Managed GC Heap", FormatHelper.FormatBytes(GC.GetTotalMemory(false)))
            | BuildMetricBlock("Tendril CPU", $"{snapshot.ProcessCpuPercent:F1}%")
            | BuildMetricBlock("Active Jobs", $"{runningJobsCount} running");

        return new Card(details).Header("Tendril Process Metrics", icon: Icons.Activity);
    }

    private static object BuildMetricBlock(string label, string value)
    {
        return Layout.Vertical().Gap(1)
            | Text.Muted(label).Small()
            | Text.Block(value).Bold();
    }

    private static string BuildSnapshotReport(SystemResourceSnapshot snapshot, int runningJobsCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# System Resource Snapshot - {snapshot.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine($"## CPU ({snapshot.Cpu.CoreCount} Cores)");
        var loadStr = (snapshot.Cpu.LoadAverage1m.HasValue && snapshot.Cpu.LoadAverage5m.HasValue && snapshot.Cpu.LoadAverage15m.HasValue)
            ? $"{snapshot.Cpu.LoadAverage1m.Value:F2}, {snapshot.Cpu.LoadAverage5m.Value:F2}, {snapshot.Cpu.LoadAverage15m.Value:F2}"
            : "N/A";

        sb.AppendLine($"- Total Usage: {snapshot.Cpu.UsagePercent:F1}%");
        sb.AppendLine($"- Load Average (1m, 5m, 15m): {loadStr}");
        sb.AppendLine($"- Process CPU: {snapshot.ProcessCpuPercent:F1}%");
        sb.AppendLine();
        sb.AppendLine("## Memory");
        sb.AppendLine($"- RAM: {FormatHelper.FormatBytes(snapshot.Memory.UsedPhysicalBytes)} / {FormatHelper.FormatBytes(snapshot.Memory.TotalPhysicalBytes)} ({snapshot.Memory.UsagePercent:F1}%)");
        sb.AppendLine($"- Free RAM: {FormatHelper.FormatBytes(snapshot.Memory.FreePhysicalBytes)}");
        sb.AppendLine($"- Swap: {FormatHelper.FormatBytes(snapshot.Memory.UsedSwapBytes)} / {FormatHelper.FormatBytes(snapshot.Memory.TotalSwapBytes)} ({snapshot.Memory.SwapUsagePercent:F1}%)");
        sb.AppendLine();
        sb.AppendLine("## GPU");
        sb.AppendLine($"- Detected: {snapshot.Gpu.IsDetected}");
        sb.AppendLine($"- Device: {snapshot.Gpu.DeviceName}");
        if (snapshot.Gpu.TotalMemoryBytes.HasValue)
        {
            sb.AppendLine($"- VRAM: {FormatHelper.FormatBytes(snapshot.Gpu.TotalMemoryBytes.Value)}");
        }
        sb.AppendLine();
        sb.AppendLine("## Disks");
        foreach (var d in snapshot.Disks)
        {
            sb.AppendLine($"- {d.Name} ({d.VolumeLabel}): {FormatHelper.FormatBytes(d.UsedBytes)} / {FormatHelper.FormatBytes(d.TotalBytes)} ({d.UsagePercent:F1}%)");
        }
        sb.AppendLine();
        sb.AppendLine("## Tendril Process");
        sb.AppendLine($"- Working Set: {FormatHelper.FormatBytes(snapshot.ProcessMemoryBytes)}");
        sb.AppendLine($"- Managed GC Heap: {FormatHelper.FormatBytes(GC.GetTotalMemory(false))}");
        sb.AppendLine($"- Active Background Jobs: {runningJobsCount}");

        return sb.ToString();
    }
}
