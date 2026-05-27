namespace Ivy.Tendril.Apps.Settings;

public enum TestStatus { Pending, Running, Passed, Failed, Warning }

public record AgentTestResult(string Label, TestStatus Status, string? Message = null, string? RawOutput = null);
