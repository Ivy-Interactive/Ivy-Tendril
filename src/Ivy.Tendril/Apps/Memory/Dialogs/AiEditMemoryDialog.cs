using System;
using Ivy;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class AiEditMemoryDialog(
    IState<bool> isOpen,
    string noteName,
    Action<string, string> onStartJob,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        var instructionsState = UseState("");

        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader($"AI Edit Memory: {noteName}"),
            new DialogBody(
                Layout.Vertical().Gap(3)
                | Text.Muted("Provide instructions for the background AI agent to edit or enhance this memory note.")
                | instructionsState.ToTextInput("e.g. Add technical details about the new auth provider endpoints...").Multiline()
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Start AI Edit").Primary().Icon(Icons.Sparkles).OnClick(() =>
                  {
                      var prompt = instructionsState.Value.Trim();
                      if (string.IsNullOrEmpty(prompt))
                      {
                          client.Toast("Please enter instructions for the AI agent.", "Input Required");
                          return;
                      }

                      onStartJob(noteName, prompt);
                      isOpen.Set(false);
                      client.Toast($"Started EditMemory job for note '{noteName}'", "Job Queued");
                  })
            )
        ).Width(Size.Rem(40));
    }
}
