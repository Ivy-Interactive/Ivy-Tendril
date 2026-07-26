using System;
using Ivy.Tendril.Apps.Views;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Library.Dialogs;

public class AiEditMemoryDialog(
    IState<bool> isOpen,
    string memoryName,
    Action<string, string> onStartAiEditJob,
    IClientProvider client) : ViewBase
{
    public override object? Build()
    {
        var instructions = UseState("");

        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader($"AI Edit: {memoryName}"),
            new DialogBody(
                Layout.Vertical()
                | Text.Muted("Instruct the agent to update this memory note. The agent will read the current note, analyze the context, make the requested modifications, and commit/sync the updates back to the vault.")
                | new Spacer().Height(Size.Units(2))
                | instructions.ToTextareaInput("e.g. Add details about the BackdropFilter fallback for Safari, or update the Three.js version info to r128.")
                    .Rows(6)
                    .WithField()
                    .Label("Instructions / Prompt")
                    .Required()
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => isOpen.Set(false)),
                new Button("Submit").Primary().Icon(Icons.Sparkles).OnClick(() =>
                {
                    var text = instructions.Value?.Trim();
                    if (string.IsNullOrEmpty(text))
                    {
                        client.Toast("Instructions cannot be empty", "Validation Error");
                        return;
                    }

                    onStartAiEditJob(memoryName, text);
                    isOpen.Set(false);
                    instructions.Set("");
                })
            )
        );
    }
}
