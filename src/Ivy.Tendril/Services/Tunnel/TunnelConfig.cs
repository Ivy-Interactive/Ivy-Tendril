namespace Ivy.Tendril.Services.Tunnel;

public record TunnelConfig
{
    public bool Enabled { get; set; }
    public int Port { get; set; } = 5010;
    public string BinaryPath { get; set; } = "";
    public int MaxRestarts { get; set; } = 10;
}
