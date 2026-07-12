using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Library.Dialogs;

public class CreateMemoryNoteDialog(
    IState<bool> isOpen,
    IState<string?> selectedNote,
    Action<string, string> onRunCommand,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        var newNoteName = UseState("");
        var newNoteTitle = UseState("");
        var newNoteTags = UseState("");

        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Create Memory Note"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | newNoteName.ToTextInput("e.g. authentication-flow")
                    .WithField()
                    .Label("Note Name")
                    .Required()
                | newNoteTitle.ToTextInput("e.g. Authentication Flow")
                    .WithField()
                    .Label("Title (Optional)")
                | newNoteTags.ToTextInput("e.g. auth, flow, security")
                    .WithField()
                    .Label("Tags (Optional, comma-separated)")
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Create").Primary().OnClick(() =>
                {
                    var name = newNoteName.Value?.Trim().Replace(" ", "-").ToLowerInvariant();
                    if (string.IsNullOrEmpty(name))
                    {
                        client.Toast("Name is required", "Validation Error");
                        return;
                    }
                    
                    var args = $"add {name}";
                    var titleVal = newNoteTitle.Value?.Trim();
                    if (!string.IsNullOrEmpty(titleVal))
                    {
                        args += $" --title \"{titleVal}\"";
                    }
                    var tagsVal = newNoteTags.Value?.Trim();
                    if (!string.IsNullOrEmpty(tagsVal))
                    {
                        args += $" --tags \"{tagsVal}\"";
                    }

                    onRunCommand("add", args);
                    isOpen.Set(false);
                    newNoteName.Set("");
                    newNoteTitle.Set("");
                    newNoteTags.Set("");
                    selectedNote.Set(name);
                })
            )
        );
    }
}
