namespace Ivy.Tendril.Services;

public class EditorNotAvailableException(string command, string label)
    : Exception($"Editor command '{command}' ({label}) is not available in PATH.")
{
    public string Command { get; } = command;
    public string Label { get; } = label;
}
