using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;
using System.IO;

namespace Ivy.Tendril.Apps.Library.Dialogs;

public class DeleteMemoryNoteDialog(
    IState<bool> isOpen,
    IState<string?> selectedNote,
    string memoriesDir,
    Action onLoadStatus,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        if (!isOpen.Value || selectedNote.Value == null) return null;

        var noteToDelete = selectedNote.Value;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Delete Memory Note"),
            new DialogBody(
                Text.P($"Are you sure you want to permanently delete memory note '{noteToDelete}'?")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Delete").Destructive().OnClick(() =>
                {
                    var path = Path.Combine(memoriesDir, noteToDelete + ".md");
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Delete(path);
                            client.Toast($"Memory note '{noteToDelete}' deleted.", "Deleted");
                        }
                        catch (Exception ex)
                        {
                            client.Toast($"Failed to delete file: {ex.Message}", "Error");
                        }
                    }
                    isOpen.Set(false);
                    selectedNote.Set(null);
                    onLoadStatus();
                })
            )
        );
    }
}
