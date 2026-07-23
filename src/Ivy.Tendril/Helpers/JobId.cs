namespace Ivy.Tendril.Helpers;

/// <summary>
/// Job ids are allocated as zero-padded 5-digit numbers by <see cref="JobIdAllocator" />. Users and agents
/// routinely type the unpadded form, so every surface that accepts a job id normalizes it here — the CLI,
/// the REST controller and the bug reporter must agree on what "458" means.
/// </summary>
public static class JobId
{
    public static string Normalize(string input)
    {
        input = input.Trim();
        return int.TryParse(input, out var num) ? num.ToString("D5") : input;
    }

    /// <summary>
    /// A job id is alphanumeric. Anything with punctuation is a caller error — most often an unsubstituted
    /// <c>{{TendrilJobId}}</c> placeholder, or a path traversal attempt. Rejecting it keeps such values out
    /// of the filenames we build from them.
    /// </summary>
    public static bool IsValid(string jobId) =>
        jobId.Length is > 0 and <= 32 && jobId.All(char.IsLetterOrDigit);
}
