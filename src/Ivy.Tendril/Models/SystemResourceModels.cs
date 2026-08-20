namespace Ivy.Tendril.Models;

public record CpuMetrics(
    double UsagePercent,
    int CoreCount,
    double? LoadAverage1m = null,
    double? LoadAverage5m = null,
    double? LoadAverage15m = null
);

public record MemoryMetrics(
    long TotalPhysicalBytes,
    long UsedPhysicalBytes,
    long FreePhysicalBytes,
    double UsagePercent,
    long TotalSwapBytes = 0,
    long UsedSwapBytes = 0,
    double SwapUsagePercent = 0.0
);

public record DiskPartitionMetrics(
    string Name,
    string VolumeLabel,
    string DriveType,
    string FileSystem,
    long TotalBytes,
    long FreeBytes,
    long UsedBytes,
    double UsagePercent
);

public record GpuMetrics(
    bool IsDetected,
    string DeviceName,
    long? TotalMemoryBytes = null,
    long? UsedMemoryBytes = null,
    string? DriverVersion = null
);

public record SystemResourceSnapshot(
    DateTime Timestamp,
    CpuMetrics Cpu,
    MemoryMetrics Memory,
    List<DiskPartitionMetrics> Disks,
    GpuMetrics Gpu,
    long ProcessMemoryBytes,
    double ProcessCpuPercent
);

public record ResourceDataPoint(
    DateTime Time,
    double CpuPercent,
    double MemoryPercent
);
