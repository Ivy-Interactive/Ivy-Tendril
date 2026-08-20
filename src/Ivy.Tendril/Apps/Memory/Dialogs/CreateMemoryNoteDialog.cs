using Ivy;
using Ivy.Tendril.Services.Memory;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class CreateMemoryNoteDialog(
    IState<bool> isOpen,
    IState<string?> selectedNote,
    IClientProvider client,
    IMemoryService memoryService,
    string? workspaceDir = null,
    string? projectName = null) : ViewBase
{
    public override object? Build()
    {
        var noteNameState = UseState("");
        var noteTitleState = UseState("");

        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Create New Memory Note"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | (Layout.Vertical().Gap(1)
                   | Text.Block("Note Identifier (kebab-case)").Small().Bold()
                   | noteNameState.ToTextInput(placeholder: "my-feature-note"))
                | (Layout.Vertical().Gap(1)
                   | Text.Block("Title (optional)").Small().Bold()
                   | noteTitleState.ToTextInput(placeholder: "My Feature Note Title"))
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Create").Primary().OnClick(() =>
                  {
                      var name = noteNameState.Value.Trim();
                      if (string.IsNullOrEmpty(name))
                      {
                          client.Toast("Note name cannot be empty.", "Error");
                          return;
                      }

                      var title = string.IsNullOrWhiteSpace(noteTitleState.Value) ? name : noteTitleState.Value;
                      memoryService.AddMemory(name, title: title, workspaceDir: workspaceDir, projectName: projectName);
                      selectedNote.Set(name);
                      isOpen.Set(false);
                      client.Toast($"Created note: {name}", "Memory Note Created");
                  })
            )
        ).Width(Size.Rem(35));
    }
}
