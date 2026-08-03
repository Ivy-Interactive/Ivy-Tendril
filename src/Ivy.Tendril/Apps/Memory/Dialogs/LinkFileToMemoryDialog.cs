using System;
using System.Collections.Generic;
using System.Linq;
using Ivy;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Memory;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class LinkFileToMemoryDialog(
    IState<bool> isOpen,
    string filePath,
    List<MemoryNote> availableNotes,
    Action onLoadStatus,
    IClientProvider client,
    IMemoryService memoryService) : ViewBase
{
    public override object? Build()
    {
        var selectedNoteName = UseState<string?>(availableNotes.FirstOrDefault()?.Name);

        if (!isOpen.Value) return null;

        object selector;
        if (availableNotes.Count == 0)
        {
            selector = Text.Muted("No memory notes exist in vault yet. Create a memory note first.");
        }
        else
        {
            var noteButtons = availableNotes.Select(note =>
            {
                var isSelected = selectedNoteName.Value == note.Name;
                var btn = isSelected ? new Button($"{note.Name} ({note.Title})").Primary() : new Button($"{note.Name} ({note.Title})").Outline();
                return btn.OnClick(() => selectedNoteName.Set(note.Name)) as object;
            }).ToArray();

            selector = Layout.Vertical().Scroll(Scroll.Auto).Height(Size.Px(200))
                       | new Fragment(noteButtons);
        }

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader($"Link Memory to Code File: {filePath}"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | Text.Muted($"Select a memory note to link to file '{filePath}':")
                | selector
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | (availableNotes.Count > 0
                   ? new Button("Link Memory").Primary().Icon(Icons.Link).OnClick(() =>
                     {
                         var noteName = selectedNoteName.Value;
                         if (string.IsNullOrEmpty(noteName))
                         {
                             client.Toast("Please select a memory note.", "Selection Required");
                             return;
                         }

                         try
                         {
                             memoryService.LinkFile(noteName, filePath);
                             isOpen.Set(false);
                             client.Toast($"Linked file '{filePath}' to note '{noteName}'", "Memory Linked");
                             onLoadStatus();
                         }
                         catch (Exception ex)
                         {
                             client.Toast($"Failed to link file: {ex.Message}", "Error");
                         }
                     })
                   : new Fragment())
            )
        ).Width(Size.Rem(40));
    }
}
