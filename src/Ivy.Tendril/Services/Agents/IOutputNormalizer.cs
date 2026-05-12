namespace Ivy.Tendril.Services.Agents;

public interface IOutputNormalizer
{
    IReadOnlyList<string> Normalize(string rawLine);
    IReadOnlyList<string> Flush();
}
