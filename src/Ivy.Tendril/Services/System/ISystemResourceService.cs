using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Services.SystemResources;

public interface ISystemResourceService
{
    /// <summary>
    ///     Captures a current snapshot of system resources (CPU, Memory, Disk, GPU, Process).
    /// </summary>
    Task<SystemResourceSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    ///     Observable stream of periodic system resource snapshots.
    /// </summary>
    IObservable<SystemResourceSnapshot> Snapshots { get; }

    /// <summary>
    ///     Gets recent historical data points for sparklines and charts (last 30-60 points).
    /// </summary>
    IReadOnlyList<ResourceDataPoint> GetHistory();
}
