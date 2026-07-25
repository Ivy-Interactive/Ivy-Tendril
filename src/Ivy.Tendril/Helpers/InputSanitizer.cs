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

    private static readonly Regex ProjectNamePattern =
        new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public static bool IsValidProjectName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;
        if (trimmed is "." or "..") return false;
        return ProjectNamePattern.IsMatch(trimmed);
    }

    public static string? DescribeProjectNameError(string? name)
    {
        if (IsValidProjectName(name)) return null;

        var suggestion = SanitizeProjectName(name ?? "");
        var suggestionClause = string.IsNullOrEmpty(suggestion) ? "" : $" Suggested: '{suggestion}'.";
        return $"Invalid project name '{name}'. Use only letters, digits, dots, dashes and underscores (no slashes or spaces).{suggestionClause}";
    }

    public static bool IsValidEmail(string emailAddress) =>
        !string.IsNullOrWhiteSpace(emailAddress) && EmailPattern.IsMatch(emailAddress);
}
