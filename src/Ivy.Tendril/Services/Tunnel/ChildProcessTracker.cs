using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Ivy.Tendril.Services.Tunnel;

/// <summary>
///     Assigns child processes to a Windows Job Object configured to kill every assigned
///     process when this (parent) process exits — including on crash, console close, or a
///     forced kill where no graceful shutdown runs. This guarantees spawned cloudflared
///     processes never outlive Tendril and accumulate as orphans across sessions.
///     No-op on non-Windows platforms.
/// </summary>
internal static class ChildProcessTracker
{
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    // Held for the lifetime of the process: when Tendril exits, the OS closes this last
    // handle to the job, which (because of KILL_ON_JOB_CLOSE) terminates every member.
    private static readonly IntPtr s_jobHandle;

    static ChildProcessTracker()
    {
        if (!OperatingSystem.IsWindows())
            return;

        s_jobHandle = CreateJobObject(IntPtr.Zero, $"Tendril.Cloudflared.{Environment.ProcessId}");
        if (s_jobHandle == IntPtr.Zero)
            return;

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var infoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, infoPtr, false);
            SetInformationJobObject(s_jobHandle, JobObjectInfoType.ExtendedLimitInformation,
                infoPtr, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    ///     Assigns a process to the kill-on-close job. Best-effort: returns false if tracking
    ///     is unavailable (non-Windows, or the assignment failed) so callers can fall back to
    ///     explicit Stop()/Dispose() without failing the tunnel.
    /// </summary>
    public static bool AddProcess(Process process)
    {
        if (!OperatingSystem.IsWindows() || s_jobHandle == IntPtr.Zero)
            return false;

        try
        {
            return AssignProcessToJobObject(s_jobHandle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? name);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType,
        IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
