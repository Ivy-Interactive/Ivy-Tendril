namespace Ivy.Tendril.Services.Agents;

public class ClaudeOutputNormalizer : IOutputNormalizer
{
    public IReadOnlyList<string> Normalize(string rawLine) => [rawLine];
    public IReadOnlyList<string> Flush() => [];
}
