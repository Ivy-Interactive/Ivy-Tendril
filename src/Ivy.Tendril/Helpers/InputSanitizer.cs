using System.Text.RegularExpressions;

namespace Ivy.Tendril.Helpers;

public static class InputSanitizer
{
    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string SanitizeProjectName(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return Regex.Replace(input, @"[^A-Za-z0-9._-]", "");
    }

    public static bool IsValidEmail(string emailAddress) =>
        !string.IsNullOrWhiteSpace(emailAddress) && EmailPattern.IsMatch(emailAddress);
}
