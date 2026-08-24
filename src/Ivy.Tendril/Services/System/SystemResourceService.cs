using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Models;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services.SystemResources;

public class SystemResourceService : ISystemResourceService, IDisposable
{
    private readonly ILogger<SystemResourceService>? _logger;
    private readonly BehaviorSubject<SystemResourceSnapshot> _subject;
    private readonly List<ResourceDataPoint> _history = new(60);
    private readonly object _lock = new();
    private readonly System.Timers.Timer _timer;

    private DateTime _lastSampleTime = DateTime.MinValue;
    private TimeSpan _lastProcCpuTime = TimeSpan.Zero;
    private long _lastSystemIdleTime;
    private long _lastSystemTotalTime;
    private GpuMetrics? _cachedGpu;
    private bool _gpuQueried;

    public SystemResourceService(ILogger<SystemResourceService>? logger = null)
    {
        _logger = logger;
        var initialSnapshot = CollectSnapshot();
        _subject = new BehaviorSubject<SystemResourceSnapshot>(initialSnapshot);

        lock (_lock)
        {
            _history.Add(new ResourceDataPoint(initialSnapshot.Timestamp, initialSnapshot.Cpu.UsagePercent, initialSnapshot.Memory.UsagePercent));
        }

        _timer = new System.Timers.Timer(2000) { AutoReset = true };
        _timer.Elapsed += (_, _) =>
        {
            try
            {
                var snapshot = CollectSnapshot();
                lock (_lock)
                {
                    if (_history.Count >= 60)
                    {
                        _history.RemoveAt(0);
                    }
                    _history.Add(new ResourceDataPoint(snapshot.Timestamp, snapshot.Cpu.UsagePercent, snapshot.Memory.UsagePercent));
                }
                _subject.OnNext(snapshot);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error refreshing system resource metrics");
            }
        };
        _timer.Start();
    }

    public IObservable<SystemResourceSnapshot> Snapshots => _subject;

    public Task<SystemResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = CollectSnapshot();
        return Task.FromResult(snapshot);
    }

    public IReadOnlyList<ResourceDataPoint> GetHistory()
    {
        lock (_lock)
        {
            return _history.ToList();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _subject.Dispose();
    }

    private SystemResourceSnapshot CollectSnapshot()
    {
        var now = DateTime.UtcNow;
        var cpu = CollectCpuMetrics(now);
        var memory = CollectMemoryMetrics();
        var disks = CollectDiskMetrics();
        var gpu = CollectGpuMetrics();
        var process = Process.GetCurrentProcess();
        var processMemoryBytes = process.WorkingSet64;

        return new SystemResourceSnapshot(
            Timestamp: now,
            Cpu: cpu,
            Memory: memory,
            Disks: disks,
            Gpu: gpu,
            ProcessMemoryBytes: processMemoryBytes,
            ProcessCpuPercent: cpu.UsagePercent
        );
    }

    private CpuMetrics CollectCpuMetrics(DateTime now)
    {
        var coreCount = Environment.ProcessorCount;
        double? load1m = null;
        double? load5m = null;
        double? load15m = null;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var loads = new double[3];
                if (GetLoadAvg(loads, 3) > 0)
                {
                    load1m = Math.Round(loads[0], 2);
                    load5m = Math.Round(loads[1], 2);
                    load15m = Math.Round(loads[2], 2);
                }
            }
            catch
            {
                // Libc load average unavailable
            }
        }

        var (systemCpuPercent, _) = ComputeCpuPercentages(now, coreCount, load1m);

        return new CpuMetrics(
            UsagePercent: systemCpuPercent,
            CoreCount: coreCount,
            LoadAverage1m: load1m,
            LoadAverage5m: load5m,
            LoadAverage15m: load15m
        );
    }

    private (double SystemPercent, double ProcessPercent) ComputeCpuPercentages(DateTime now, int coreCount, double? load1m)
    {
        double processPercent = 0.0;
        double systemPercent = 0.0;

        try
        {
            var proc = Process.GetCurrentProcess();
            var currentProcCpu = proc.TotalProcessorTime;

            if (_lastSampleTime != DateTime.MinValue && _lastSampleTime < now)
            {
                var elapsedWallMs = (now - _lastSampleTime).TotalMilliseconds;
                var elapsedProcMs = (currentProcCpu - _lastProcCpuTime).TotalMilliseconds;

                if (elapsedWallMs > 0 && coreCount > 0)
                {
                    processPercent = Math.Clamp(Math.Round((elapsedProcMs / (elapsedWallMs * coreCount)) * 100.0, 1), 0.0, 100.0);
                }
            }

            _lastProcCpuTime = currentProcCpu;
        }
        catch
        {
            // Ignore process CPU query failures
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                if (File.Exists("/proc/stat"))
                {
                    var firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine) && firstLine.StartsWith("cpu "))
                    {
                        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            long user = long.Parse(parts[1], CultureInfo.InvariantCulture);
                            long nice = long.Parse(parts[2], CultureInfo.InvariantCulture);
                            long system = long.Parse(parts[3], CultureInfo.InvariantCulture);
                            long idle = long.Parse(parts[4], CultureInfo.InvariantCulture);
                            long iowait = parts.Length > 5 ? long.Parse(parts[5], CultureInfo.InvariantCulture) : 0;
                            long irq = parts.Length > 6 ? long.Parse(parts[6], CultureInfo.InvariantCulture) : 0;
                            long softirq = parts.Length > 7 ? long.Parse(parts[7], CultureInfo.InvariantCulture) : 0;
                            long steal = parts.Length > 8 ? long.Parse(parts[8], CultureInfo.InvariantCulture) : 0;

                            long totalTime = user + nice + system + idle + iowait + irq + softirq + steal;
                            long idleTime = idle + iowait;

                            if (_lastSystemTotalTime > 0 && totalTime > _lastSystemTotalTime)
                            {
                                var deltaTotal = totalTime - _lastSystemTotalTime;
                                var deltaIdle = idleTime - _lastSystemIdleTime;
                                var usedRatio = 1.0 - ((double)deltaIdle / deltaTotal);
                                systemPercent = Math.Clamp(Math.Round(usedRatio * 100.0, 1), 0.0, 100.0);
                            }

                            _lastSystemTotalTime = totalTime;
                            _lastSystemIdleTime = idleTime;
                        }
                    }
                }
            }
            catch
            {
                // Fallback below
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
                {
                    long total = (kernelTime - _lastSystemIdleTime) + (userTime - _lastSystemTotalTime); // kernel includes idle
                    long idle = idleTime - _lastSystemIdleTime;

                    if (_lastSystemTotalTime > 0 && total > 0)
                    {
                        var usedRatio = 1.0 - ((double)idle / total);
                        systemPercent = Math.Clamp(Math.Round(usedRatio * 100.0, 1), 0.0, 100.0);
                    }

                    _lastSystemIdleTime = idleTime;
                    _lastSystemTotalTime = kernelTime + userTime;
                }
            }
            catch
            {
                // Fallback below
            }
        }

        if (systemPercent <= 0.0)
        {
            if (load1m.HasValue && coreCount > 0)
            {
                systemPercent = Math.Clamp(Math.Round((load1m.Value / coreCount) * 100.0, 1), 0.0, 100.0);
            }
            else
            {
                systemPercent = processPercent;
            }
        }

        _lastSampleTime = now;
        return (systemPercent, processPercent);
    }

    private MemoryMetrics CollectMemoryMetrics()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        long totalPhysical = gcInfo.TotalAvailableMemoryBytes;
        long usedPhysical = gcInfo.MemoryLoadBytes > 0 ? gcInfo.MemoryLoadBytes : gcInfo.HeapSizeBytes;
        long freePhysical = Math.Max(0, totalPhysical - usedPhysical);
        long totalSwap = 0;
        long usedSwap = 0;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && File.Exists("/proc/meminfo"))
        {
            try
            {
                long memTotalKb = 0, memAvailableKb = 0, swapTotalKb = 0, swapFreeKb = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:")) memTotalKb = ParseKb(line);
                    else if (line.StartsWith("MemAvailable:")) memAvailableKb = ParseKb(line);
                    else if (line.StartsWith("SwapTotal:")) swapTotalKb = ParseKb(line);
                    else if (line.StartsWith("SwapFree:")) swapFreeKb = ParseKb(line);
                }

                if (memTotalKb > 0)
                {
                    totalPhysical = memTotalKb * 1024;
                    freePhysical = memAvailableKb > 0 ? memAvailableKb * 1024 : freePhysical;
                    usedPhysical = Math.Max(0, totalPhysical - freePhysical);
                }
                if (swapTotalKb > 0)
                {
                    totalSwap = swapTotalKb * 1024;
                    long freeSwap = swapFreeKb * 1024;
                    usedSwap = Math.Max(0, totalSwap - freeSwap);
                }
            }
            catch
            {
                // Fallback to default GC metrics
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    totalPhysical = (long)memStatus.ullTotalPhys;
                    freePhysical = (long)memStatus.ullAvailPhys;
                    usedPhysical = Math.Max(0, totalPhysical - freePhysical);
                    totalSwap = (long)memStatus.ullTotalPageFile;
                    long freeSwap = (long)memStatus.ullAvailPageFile;
                    usedSwap = Math.Max(0, totalSwap - freeSwap);
                }
            }
            catch
            {
                // Fallback to default GC metrics
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var swapOutput = RunQuickCommand("sysctl", "-n vm.swapusage");
                if (!string.IsNullOrEmpty(swapOutput))
                {
                    // Format: total = 3072.00M  used = 1845.50M  free = 1226.50M
                    var totalMatch = Regex.Match(swapOutput, @"total\s*=\s*([\d\.]+)([KMGTP]?)", RegexOptions.IgnoreCase);
                    var usedMatch = Regex.Match(swapOutput, @"used\s*=\s*([\d\.]+)([KMGTP]?)", RegexOptions.IgnoreCase);
                    if (totalMatch.Success && usedMatch.Success)
                    {
                        totalSwap = ParseSizeToBytes(totalMatch.Groups[1].Value, totalMatch.Groups[2].Value);
                        usedSwap = ParseSizeToBytes(usedMatch.Groups[1].Value, usedMatch.Groups[2].Value);
                    }
                }
            }
            catch
            {
                // Fallback
            }
        }

        var usagePercent = totalPhysical > 0 ? Math.Clamp(Math.Round((double)usedPhysical / totalPhysical * 100.0, 1), 0.0, 100.0) : 0.0;
        var swapUsagePercent = totalSwap > 0 ? Math.Clamp(Math.Round((double)usedSwap / totalSwap * 100.0, 1), 0.0, 100.0) : 0.0;

        return new MemoryMetrics(
            TotalPhysicalBytes: totalPhysical,
            UsedPhysicalBytes: usedPhysical,
            FreePhysicalBytes: freePhysical,
            UsagePercent: usagePercent,
            TotalSwapBytes: totalSwap,
            UsedSwapBytes: usedSwap,
            SwapUsagePercent: swapUsagePercent
        );
    }

    private List<DiskPartitionMetrics> CollectDiskMetrics()
    {
        var disks = new List<DiskPartitionMetrics>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady || drive.TotalSize <= 0) continue;

                    var total = drive.TotalSize;
                    var free = drive.AvailableFreeSpace;
                    var used = Math.Max(0, total - free);
                    var usagePercent = total > 0 ? Math.Clamp(Math.Round((double)used / total * 100.0, 1), 0.0, 100.0) : 0.0;

                    var volumeLabel = "";
                    try { volumeLabel = drive.VolumeLabel ?? ""; } catch { }

                    var fileSystem = "";
                    try { fileSystem = drive.DriveFormat ?? ""; } catch { }

                    disks.Add(new DiskPartitionMetrics(
                        Name: drive.Name,
                        VolumeLabel: volumeLabel,
                        DriveType: drive.DriveType.ToString(),
                        FileSystem: fileSystem,
                        TotalBytes: total,
                        FreeBytes: free,
                        UsedBytes: used,
                        UsagePercent: usagePercent
                    ));
                }
                catch
                {
                    // Ignore single drive failure
                }
            }
        }
        catch
        {
            // Ignore drive enumeration failure
        }

        return disks;
    }

    private GpuMetrics CollectGpuMetrics()
    {
        if (_gpuQueried && _cachedGpu != null)
        {
            return _cachedGpu;
        }

        _gpuQueried = true;
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var output = RunQuickCommand("system_profiler", "SPDisplaysDataType");
                if (!string.IsNullOrEmpty(output))
                {
                    var match = Regex.Match(output, @"Chipset Model:\s*(.+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var name = match.Groups[1].Value.Trim();
                        var vramMatch = Regex.Match(output, @"VRAM \(Total\):\s*([\d\.]+)\s*([KMGTP]?)B", RegexOptions.IgnoreCase);
                        long? vram = null;
                        if (vramMatch.Success)
                        {
                            vram = ParseSizeToBytes(vramMatch.Groups[1].Value, vramMatch.Groups[2].Value);
                        }

                        _cachedGpu = new GpuMetrics(
                            IsDetected: true,
                            DeviceName: name,
                            TotalMemoryBytes: vram
                        );
                        return _cachedGpu;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var nvidiaOut = RunQuickCommand("nvidia-smi", "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
                if (!string.IsNullOrEmpty(nvidiaOut))
                {
                    var parts = nvidiaOut.Split(',', StringSplitOptions.TrimEntries);
                    if (parts.Length >= 1)
                    {
                        var name = parts[0];
                        long? memory = parts.Length >= 2 && long.TryParse(parts[1], out var mb) ? mb * 1024 * 1024 : null;
                        var driver = parts.Length >= 3 ? parts[2] : null;

                        _cachedGpu = new GpuMetrics(
                            IsDetected: true,
                            DeviceName: name,
                            TotalMemoryBytes: memory,
                            DriverVersion: driver
                        );
                        return _cachedGpu;
                    }
                }

                var lspciOut = RunQuickCommand("lspci", "");
                if (!string.IsNullOrEmpty(lspciOut))
                {
                    var vgaLine = lspciOut.Split('\n').FirstOrDefault(l => l.Contains("VGA compatible controller", StringComparison.OrdinalIgnoreCase) || l.Contains("3D controller", StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(vgaLine))
                    {
                        var idx = vgaLine.IndexOf(':');
                        var name = (idx >= 0 ? vgaLine[(idx + 1)..] : vgaLine).Trim();
                        _cachedGpu = new GpuMetrics(IsDetected: true, DeviceName: name);
                        return _cachedGpu;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var wmicOut = RunQuickCommand("wmic", "path win32_VideoController get name");
                if (!string.IsNullOrEmpty(wmicOut))
                {
                    var lines = wmicOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var name = lines.Skip(1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                    if (!string.IsNullOrEmpty(name))
                    {
                        _cachedGpu = new GpuMetrics(IsDetected: true, DeviceName: name);
                        return _cachedGpu;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "GPU query encountered an exception");
        }

        _cachedGpu = new GpuMetrics(IsDetected: false, DeviceName: "No GPU detected");
        return _cachedGpu;
    }

    private static string? RunQuickCommand(string command, string args)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            proc.Start();
            if (proc.WaitForExit(1500))
            {
                return proc.StandardOutput.ReadToEnd();
            }
            try { proc.Kill(); } catch { }
        }
        catch
        {
            // Tool not found or execution failed
        }
        return null;
    }

    private static long ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb) ? kb : 0;
    }

    private static long ParseSizeToBytes(string numStr, string unit)
    {
        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            return 0;

        return unit.ToUpperInvariant() switch
        {
            "K" => (long)(num * 1024),
            "M" => (long)(num * 1024 * 1024),
            "G" => (long)(num * 1024 * 1024 * 1024),
            "T" => (long)(num * 1024L * 1024L * 1024L * 1024L),
            _ => (long)num
        };
    }

    [DllImport("libc", EntryPoint = "getloadavg")]
    private static extern int GetLoadAvg([Out] double[] loadavg, int nelem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
