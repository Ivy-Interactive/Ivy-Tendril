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
                var filePath = url["file:///".Length..];
                openFileState.Set(filePath);
            }
            else if (url.StartsWith("plan://", StringComparison.OrdinalIgnoreCase))
            {
                var planIdStr = url["plan://".Length..];
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
        var client = UseService<IClientProvider>();

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
        
        var finalContent = fileExists
            ? new HeaderLayout(
                Layout.Vertical().Gap(2)
                    | new Button($"Open in {config.Editor.Label}").Icon(Icons.ExternalLink).Outline().OnClick(() =>
                    {
                        try
                        {
                            config.OpenInEditor(filePath);
                        }
                        catch (EditorNotAvailableException ex)
                        {
                            client.Toast(
                                $"'{ex.Command}' not found in PATH. Install the shell command from {ex.Label} or update the editor command in Settings → Advanced.",
                                "Editor Not Available",
                                variant: ToastVariant.Destructive);
                        }
                    })
                    | Text.Block(filePath).Muted()
                ,
                sheetContent
            )
            : sheetContent;

        return new Sheet(
            () => openFile.Set(null),
            finalContent,
            Path.GetFileName(filePath)
        ).Width(Size.Half()).Resizable();
    }
}
