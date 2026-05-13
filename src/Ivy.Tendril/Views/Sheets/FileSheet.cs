using System.Diagnostics;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Views.Sheets;

public class FileSheet(
    IState<string?> openFile,
    IConfigService config) : ViewBase
{
    public static Action<string> CreateLinkClickHandler(
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        };
    }

    public override object Build()
    {
        if (openFile.Value is not { } filePath)
            return new Empty();

        var ext = Path.GetExtension(filePath);
        var fileExists = File.Exists(filePath);
        object sheetContent;

        if (FileHelper.IsImageExtension(ext))
        {
            var imageUrl = $"/ivy/local-file?path={Uri.EscapeDataString(filePath)}";
            sheetContent = new Image(imageUrl) { ObjectFit = ImageFit.Contain, Alt = Path.GetFileName(filePath) };
        }
        else if (fileExists)
        {
            var fileContent = FileHelper.ReadAllText(filePath);
            var language = FileHelper.GetLanguage(ext);
            sheetContent = new Markdown($"```{language.ToString().ToLowerInvariant()}\n{fileContent}\n```");
        }
        else
        {
            sheetContent = new Markdown("File not found.");
        }

        var contentWithPath = Layout.Vertical().Gap(1);
        contentWithPath |= Text.Block(filePath).Muted().Small();
        contentWithPath |= sheetContent;

        var finalContent = fileExists
            ? new HeaderLayout(
                new Button($"Open in {config.Editor.Label}").Icon(Icons.ExternalLink).Outline().OnClick(() =>
                {
                    config.OpenInEditor(filePath);
                }),
                contentWithPath
            )
            : sheetContent;

        return new Sheet(
            () => openFile.Set(null),
            finalContent,
            Path.GetFileName(filePath)
        ).Width(Size.Half()).Resizable();
    }
}
