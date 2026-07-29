using System;

namespace Ivy.Tendril.Models;

public record ConnectionItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Provider { get; init; } = ""; // e.g. "Slack", "Discord", "GitHub"
    public string ConnectionString { get; init; } = ""; // JSON configuration (Token, etc.)
    public string Permissions { get; init; } = ""; // Comma-separated list of allowed actions, or "*" for all
    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }
}
