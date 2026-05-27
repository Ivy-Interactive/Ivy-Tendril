namespace Ivy.Tendril.Apps.Settings.Dialogs;

public class AgentTestDialog(
    IState<bool> isOpen,
    IState<List<AgentTestResult>?> testResults,
    IState<bool> isTesting,
    IState<CancellationTokenSource?> testCts) : ViewBase
{
    public override object? Build()
    {
        var debugOutput = UseState<string?>(null);
        var results = testResults.Value;

        if (!isOpen.Value) return null;

        var body = Layout.Vertical().Gap(2);

        if (results is { Count: > 0 })
        {
            var table = new Table();

            foreach (var r in results)
            {
                object statusIcon = r.Status switch
                {
                    TestStatus.Passed => Icons.CircleCheck.ToIcon().Color(Colors.Success),
                    TestStatus.Failed => Icons.CircleX.ToIcon().Color(Colors.Destructive),
                    TestStatus.Warning => Icons.Info.ToIcon().Color(Colors.Warning),
                    TestStatus.Running => new Loading(),
                    _ => Icons.CircleDashed.ToIcon().Color(Colors.Muted)
                };

                object? messageCell = r.Message != null ? Text.Muted(r.Message) : null;

                object? debugCell = r.RawOutput != null
                    ? new Button().Icon(Icons.Bug).Outline().Small()
                        .Tooltip("Show raw output")
                        .OnClick(() => debugOutput.Set(
                            debugOutput.Value == r.RawOutput ? null : r.RawOutput))
                    : null;

                table |= new TableRow(
                    new TableCell(statusIcon),
                    new TableCell(Text.Block(r.Label)),
                    new TableCell(messageCell),
                    new TableCell(debugCell)
                );
            }

            body |= table;
        }

        if (debugOutput.Value != null)
        {
            body |= new Separator();
            body |= Text.Block("Raw Output").Bold().Small();
            body |= Text.Code(debugOutput.Value).Small();
        }

        void CloseDialog()
        {
            testCts.Value?.Cancel();
            isOpen.Set(false);
            debugOutput.Set(null);
        }

        return new Dialog(
            _ => CloseDialog(),
            new DialogHeader("Agent Test Results"),
            new DialogBody(body),
            new DialogFooter(
                isTesting.Value
                    ? new Button("Cancel").Outline().OnClick(() => testCts.Value?.Cancel())
                    : new Button("Close").Outline().OnClick(CloseDialog)
            )
        ).Width(Size.Rem(40));
    }
}
