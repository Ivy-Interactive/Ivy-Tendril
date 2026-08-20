using System;
using Ivy;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class CompactMemoriesDialog(
    IState<bool> isOpen,
    Action onCompactConfirmed) : ViewBase
{
    public override object? Build()
    {
        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Compact Memory Logs"),
            new DialogBody(
                Text.Block("This will scan memory notes, distill long log sections, and archive old execution traces to optimize memory storage. Proceed?")
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Compact Logs").Primary().Icon(Icons.Minimize2).OnClick(() =>
                  {
                      onCompactConfirmed();
                      isOpen.Set(false);
                  })
            )
        ).Width(Size.Rem(35));
    }
}
