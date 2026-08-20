using System;
using Ivy;

namespace Ivy.Tendril.Apps.Memory.Dialogs;

public class PurgeMemoriesDialog(
    IState<bool> isOpen,
    Action onPurgeConfirmed) : ViewBase
{
    public override object? Build()
    {
        if (!isOpen.Value) return null;

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Purge All Memories"),
            new DialogBody(
                Text.Block("This will delete memory notes for the current project or vault. This action cannot be undone. Proceed?")
            ),
            new DialogFooter(
                Layout.Horizontal().Gap(2).Right()
                | new Button("Cancel").Outline().OnClick(() => isOpen.Set(false))
                | new Button("Purge All").Destructive().OnClick(() =>
                  {
                      onPurgeConfirmed();
                      isOpen.Set(false);
                  })
            )
        ).Width(Size.Rem(35));
    }
}
