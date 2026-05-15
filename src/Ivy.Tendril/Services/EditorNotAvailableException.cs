namespace Ivy.Tendril.Services;

public class EditorNotAvailableException : Exception
{
    public string Command { get; }
    public string Label { get; }

    public EditorNotAvailableException(string command, string label)
        : base($"Editor command '{command}' ({label}) is not available in PATH.")
    {
        Command = command;
        Label = label;
    }
}
