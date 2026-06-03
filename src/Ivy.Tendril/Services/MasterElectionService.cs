using System.Diagnostics;
using System.Text.Json;
using Ivy.Tendril.Helpers;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ivy.Tendril.Services;

public interface IMasterElectionService : IDisposable
{
    bool IsMaster { get; }
}

public class MasterElectionService(
    IConfigService configService,
    IHostApplicationLifetime appLifetime,
    IServer server,
    ILogger<MasterElectionService> logger)
    : IMasterElectionService, IStartable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private Timer? _heartbeatTimer;
    private string? _masterFilePath;

    public bool IsMaster { get; private set; }

    public void Start()
    {
        if (Environment.GetEnvironmentVariable("TENDRIL_NOT_MASTER") == "1")
        {
            logger.LogInformation("Running in non-master mode (TENDRIL_NOT_MASTER=1)");
            return;
        }

        if (string.IsNullOrEmpty(configService.TendrilHome))
            return;

        _masterFilePath = Path.Combine(configService.TendrilHome, ".master");

        if (!TryClaim())
        {
            logger.LogWarning("Another Tendril master is running. This instance will not accept CLI commands.");
            return;
        }

        IsMaster = true;

        appLifetime.ApplicationStarted.Register(OnApplicationStarted);
        appLifetime.ApplicationStopping.Register(OnApplicationStopping);
    }

    private void OnApplicationStarted()
    {
        var port = GetBoundPort();
        if (port == null)
        {
            logger.LogWarning("Could not determine bound port — .master file not written");
            IsMaster = false;
            return;
        }

        WriteMasterFile(port.Value);
        _heartbeatTimer = new Timer(UpdateHeartbeat, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        logger.LogInformation("Master election won — listening on port {Port}", port.Value);
    }

    private void OnApplicationStopping()
    {
        Cleanup();
    }

    private bool TryClaim()
    {
        if (_masterFilePath == null) return false;

        if (!File.Exists(_masterFilePath))
            return true;

        try
        {
            var json = File.ReadAllText(_masterFilePath);
            var existing = JsonSerializer.Deserialize<MasterFileData>(json, JsonOptions);
            if (existing == null)
            {
                File.Delete(_masterFilePath);
                return true;
            }

            if (!IsProcessAlive(existing.Pid))
            {
                logger.LogInformation("Stale .master file (PID {Pid} is dead) — claiming master", existing.Pid);
                File.Delete(_masterFilePath);
                return true;
            }

            if (DateTime.UtcNow - existing.Heartbeat > TimeSpan.FromSeconds(90))
            {
                logger.LogInformation("Stale .master file (heartbeat expired) — claiming master");
                File.Delete(_masterFilePath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read .master file — deleting and claiming");
            try { File.Delete(_masterFilePath); } catch { }
            return true;
        }
    }

    private void WriteMasterFile(int port)
    {
        if (_masterFilePath == null) return;

        var data = new MasterFileData
        {
            Pid = Environment.ProcessId,
            Port = port,
            StartedAt = DateTime.UtcNow,
            Heartbeat = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        FileHelper.WriteAllText(_masterFilePath, json);
    }

    private void UpdateHeartbeat(object? state)
    {
        if (_masterFilePath == null || !IsMaster) return;

        try
        {
            if (!File.Exists(_masterFilePath)) return;

            var json = File.ReadAllText(_masterFilePath);
            var data = JsonSerializer.Deserialize<MasterFileData>(json, JsonOptions);
            if (data == null || data.Pid != Environment.ProcessId) return;

            data.Heartbeat = DateTime.UtcNow;
            json = JsonSerializer.Serialize(data, JsonOptions);
            FileHelper.WriteAllText(_masterFilePath, json);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to update heartbeat");
        }
    }

    private int? GetBoundPort()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses == null || addresses.Count == 0) return null;

        var address = addresses.First();
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return uri.Port;

        var lastColon = address.LastIndexOf(':');
        if (lastColon >= 0 && int.TryParse(address[(lastColon + 1)..], out var port))
            return port;

        return null;
    }

    private void Cleanup()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        if (_masterFilePath != null && IsMaster)
        {
            try
            {
                if (File.Exists(_masterFilePath))
                {
                    var json = File.ReadAllText(_masterFilePath);
                    var data = JsonSerializer.Deserialize<MasterFileData>(json, JsonOptions);
                    if (data?.Pid == Environment.ProcessId)
                        File.Delete(_masterFilePath);
                }
            }
            catch
            {
                // ignored
            }
        }

        IsMaster = false;
    }

    public void Dispose() => Cleanup();

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var proc = Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch
        {
            return false;
        }
    }

    public record MasterFileData
    {
        public int Pid { get; set; }
        public int Port { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime Heartbeat { get; set; }
    }
}
