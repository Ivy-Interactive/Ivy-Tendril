using System;
using System.Collections.Generic;

namespace Ivy.Tendril.Models;

public record WorkflowItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Project { get; init; } = "default";
    public string Definition { get; init; } = ""; // JSON string representation of steps
    public bool IsActive { get; init; } = true;
    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }
}

public class WorkflowStep
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = ""; // "Trigger", "Connection", "Prompt"
    public string Provider { get; set; } = ""; // E.g. Slack, GitHub, Discord, claude, gemini
    public string ConnectionName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Args { get; set; } = ""; // Prompt text or JSON arguments template
    public string Model { get; set; } = "";
    public List<string> Next { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
}

public class WorkflowDefinition
{
    public List<WorkflowStep> Steps { get; set; } = new();
}
