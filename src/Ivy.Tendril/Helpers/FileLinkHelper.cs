using System.Diagnostics;
using Ivy.Tendril.Apps;

using Ivy.Tendril.Services;
namespace Ivy.Tendril.Helpers;

public static class FileLinkHelper
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"];

    public static Action<string> CreateFileLinkClickHandler(
        IState<string?> openFileState,
        Action<int>? onPlanClick = null)
    {
        return url =>
        {
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                var filePath = url.Substring("file:///".Length);
                openFileState.Set(filePath);
            }
            else if (url.StartsWith("plan://", StringComparison.OrdinalIgnoreCase))
            {
                var planIdStr = url.Substring("plan://".Length);
                if (int.TryParse(planIdStr, out var planId))
                    onPlanClick?.Invoke(planId);
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Open external links in system default browser
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        };
    }

    public static object? BuildFileLinkSheet(
        string? filePath,
        Action onClose,
        IEnumerable<string> repoPaths,
        IConfigService config)
    {
        if (filePath is null)
            return null;

        var ext = Path.GetExtension(filePath);
        object sheetContent;

        if (ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
            var imageUrl = $"/ivy/local-file?path={Uri.EscapeDataString(filePath)}";
            sheetContent = new Image(imageUrl) { ObjectFit = ImageFit.Contain, Alt = Path.GetFileName(filePath) };
        }
        else
        {
            if (File.Exists(filePath))
            {
                var fileContent = FileHelper.ReadAllText(filePath);
                var language = FileApp.GetLanguage(ext);
                sheetContent = new Markdown($"```{language.ToString().ToLowerInvariant()}\n{fileContent}\n```");
            }
            else
            {
                sheetContent = new Markdown("File not found.");
            }
        }

        var finalContent = File.Exists(filePath)
            ? new HeaderLayout(
                new Button($"Open in {config.Editor.Label}").Icon(Icons.ExternalLink).Outline().OnClick(() =>
                {
                    config.OpenInEditor(filePath);
                }),
                sheetContent
            )
            : sheetContent;

        return new Sheet(
            onClose,
            finalContent,
            Path.GetFileName(filePath)
        ).Width(Size.Half()).Resizable();
    }
}