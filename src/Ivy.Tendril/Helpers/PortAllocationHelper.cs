using System.Net;
using System.Net.Sockets;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Helpers;

/// <summary>
///     Assigns a concrete TCP port to each of a project's named service ports for one plan.
///     A multi-service repo binds static defaults (3000/3001/3002), so a host process already on
///     one of them — or a second plan under review at the same time — makes the whole stack fail
///     with <c>EADDRINUSE</c>. Assignments are recorded in <see cref="PlanYaml.AllocatedPorts" />
///     so a review session keeps stable URLs across re-executions.
/// </summary>
public static class PortAllocationHelper
{
    /// <summary>First port scanned when a service's default port is unavailable.</summary>
    public const int EphemeralRangeStart = 30000;

    /// <summary>Last port scanned (inclusive) when a service's default port is unavailable.</summary>
    public const int EphemeralRangeEnd = 45000;

    /// <summary>
    ///     Plan states whose <see cref="PlanYaml.AllocatedPorts" /> are treated as taken. A finished
    ///     plan's worktree is gone, so holding its ports back would exhaust the range over time.
    /// </summary>
    private static readonly HashSet<string> ActiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(PlanStatus.Draft), nameof(PlanStatus.Creating), nameof(PlanStatus.Updating),
        nameof(PlanStatus.Executing), nameof(PlanStatus.Review), nameof(PlanStatus.Blocked)
    };

    /// <summary>
    ///     True when a listener can bind <paramref name="port" /> on loopback. Only loopback is probed:
    ///     review actions and verifications bind 127.0.0.1 (binding 0.0.0.0 is blocked on some hosts),
    ///     so a service occupying only an external interface must not count as a collision.
    /// </summary>
    public static bool IsPortAvailable(int port)
    {
        if (port is < 1 or > 65535) return false;

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    /// <summary>
    ///     Ensures every named port in <paramref name="project" /> has an assignment for
    ///     <paramref name="plan" />, persisting <c>plan.yaml</c> when anything changed, and returns the
    ///     full mapping. Ports already recorded on the plan are retained when still free, so the URLs a
    ///     reviewer has open survive a re-execution.
    /// </summary>
    /// <param name="planFolder">
    ///     The plan's folder. Its parent is used to find sibling plans whose ports are already taken.
    /// </param>
    public static Dictionary<string, int> AllocatePorts(
        ProjectConfig project, PlanYaml plan, string planFolder, IPlanWatcherService? watcher = null)
    {
        if (project.Ports.Count == 0)
            return plan.AllocatedPorts is null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(plan.AllocatedPorts);

        var plansDir = Path.GetDirectoryName(Path.GetFullPath(planFolder.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var reserved = plansDir is null
            ? new HashSet<int>()
            : CollectReservedPorts(plansDir, planFolder);

        var resolved = ResolvePorts(project, plan.AllocatedPorts, reserved, IsPortAvailable);

        if (!AreEqual(plan.AllocatedPorts, resolved))
        {
            plan.AllocatedPorts = resolved;
            plan.Updated = DateTime.UtcNow;
            PlanCommandHelpers.WritePlan(planFolder, plan, watcher);
        }

        return resolved;
    }

    /// <summary>
    ///     Pure allocation logic, separated from disk and socket access so it can be exercised
    ///     deterministically. Iterates the project's ports in configuration order.
    /// </summary>
    /// <param name="existing">Ports already recorded on the plan, retained when still free.</param>
    /// <param name="reserved">Ports held by other plans or by earlier names in this same pass.</param>
    /// <param name="isAvailable">Availability probe (<see cref="IsPortAvailable" /> in production).</param>
    internal static Dictionary<string, int> ResolvePorts(
        ProjectConfig project,
        IReadOnlyDictionary<string, int>? existing,
        ISet<int> reserved,
        Func<int, bool> isAvailable)
    {
        var taken = new HashSet<int>(reserved);
        var resolved = new Dictionary<string, int>();

        foreach (var (name, portConfig) in project.Ports)
        {
            if (existing != null &&
                existing.TryGetValue(name, out var previous) &&
                !taken.Contains(previous) &&
                isAvailable(previous))
            {
                resolved[name] = previous;
                taken.Add(previous);
                continue;
            }

            var assigned = PickPort(portConfig.DefaultPort, taken, isAvailable);
            if (assigned == 0)
                throw new InvalidOperationException(
                    $"No free TCP port available for '{name}': the default port {portConfig.DefaultPort} is in " +
                    $"use and the range {EphemeralRangeStart}-{EphemeralRangeEnd} is exhausted.");

            resolved[name] = assigned;
            taken.Add(assigned);
        }

        // Names dropped from the project config keep no reservation, but a plan that recorded them is
        // not rewritten to lose data an in-flight worktree may still be using.
        if (existing != null)
            foreach (var (name, port) in existing)
                resolved.TryAdd(name, port);

        return resolved;
    }

    /// <summary>Returns the default port when usable, otherwise the first free ephemeral port (0 if none).</summary>
    private static int PickPort(int defaultPort, ICollection<int> taken, Func<int, bool> isAvailable)
    {
        if (defaultPort is > 0 and <= 65535 && !taken.Contains(defaultPort) && isAvailable(defaultPort))
            return defaultPort;

        for (var candidate = EphemeralRangeStart; candidate <= EphemeralRangeEnd; candidate++)
        {
            if (taken.Contains(candidate)) continue;
            if (isAvailable(candidate)) return candidate;
        }

        return 0;
    }

    /// <summary>
    ///     Collects the ports recorded by every other plan in <paramref name="plansDir" /> that is still
    ///     active, so two concurrent reviews never receive the same port. Unreadable plans are skipped:
    ///     a corrupt neighbour must not block allocation.
    /// </summary>
    internal static HashSet<int> CollectReservedPorts(string plansDir, string? excludePlanFolder)
    {
        var reserved = new HashSet<int>();
        if (!Directory.Exists(plansDir)) return reserved;

        var excluded = excludePlanFolder is null
            ? null
            : Path.GetFullPath(excludePlanFolder.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        foreach (var folder in Directory.EnumerateDirectories(plansDir))
        {
            if (excluded != null &&
                Path.GetFullPath(folder).Equals(excluded, StringComparison.OrdinalIgnoreCase))
                continue;

            PlanYaml? other;
            try
            {
                other = PlanYamlHelper.ReadPlanYaml(folder);
            }
            catch
            {
                continue;
            }

            if (other?.AllocatedPorts is not { Count: > 0 }) continue;
            if (!ActiveStates.Contains(other.State ?? "")) continue;

            foreach (var port in other.AllocatedPorts.Values)
                reserved.Add(port);
        }

        return reserved;
    }

    private static bool AreEqual(IReadOnlyDictionary<string, int>? a, IReadOnlyDictionary<string, int> b)
    {
        if (a is null) return b.Count == 0;
        if (a.Count != b.Count) return false;
        return a.All(kv => b.TryGetValue(kv.Key, out var value) && value == kv.Value);
    }
}
