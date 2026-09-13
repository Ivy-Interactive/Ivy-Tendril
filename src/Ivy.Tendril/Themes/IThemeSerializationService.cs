using Ivy;

namespace Ivy.Tendril.Themes;

public interface IThemeSerializationService
{
    bool TryImportTheme(string raw, out Theme importedTheme, out string? errorMessage);
    string ExportToCSharp(Theme theme);
    string ExportToJson(Theme theme, bool indented = true);
}
