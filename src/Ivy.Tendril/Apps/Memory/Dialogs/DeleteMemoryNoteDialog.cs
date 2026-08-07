using System;
using Ivy;
using Ivy.Tendril.Services.Memory;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class DeleteMemoryNoteDialog(
    IState<bool> isOpen,
    IState<string?> selectedNote,
    Action onLoadStatus,
    IClientProvider client,
    IMemoryService memoryService,
    string? workspaceDir = null,
    string? projectName = null) : ViewBase
{
    public override object? Build()
    {
        if (!isOpen.Value) return null;

        var targetNote = selectedNote.Value ?? "";

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Delete Memory Note"),
            new DialogBody(
                Text.Block($"Are you sure you want to delete note '{targetNote}'? This action cannot be undone.")
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Delete").Destructive().OnClick(() =>
                  {
                      memoryService.DeleteMemory(targetNote, workspaceDir: workspaceDir, projectName: projectName);
                      selectedNote.Set(null);
                      isOpen.Set(false);
                      client.Toast($"Deleted note: {targetNote}", "Memory Note Deleted");
                      onLoadStatus();
                  })
            )
        ).Width(Size.Rem(35));
    }
}
