using System.Collections.Immutable;
using Ivy.Tendril.Models;

namespace Ivy.Tendril.Apps.ReviewAction;

/// <summary>
///     Confirms sending the comments left on the running app back to the agent as a change
///     request — the same <see cref="RetryPlanArgs"/> path as "Request Changes" on the plan's
///     diff, so this feedback arrives exactly like line-by-line feedback does.
///
///     It asks first rather than firing on the button because the agent then rewrites the
///     branch: worth one look at what is about to be sent, and the list is the only place the
///     reviewer sees all their comments together.
/// </summary>
public class UpdateFromCommentsDialog(
    IState<bool> dialogOpen,
    PlanFile plan,
    string appUrl,
    IState<ImmutableList<AppComment>> comments,
    IJobService jobService,
    Action onSubmitted) : ViewBase
{
    public override object? Build()
    {
        if (!dialogOpen.Value) return null;

        var pending = comments.Value;
        if (pending.IsEmpty)
        {
            dialogOpen.Set(false);
            return null;
        }

        var lines = pending.Select(comment =>
        {
            var where = AppPreview.SourceLabel(comment.DebugJson);
            var tag = string.IsNullOrEmpty(comment.Tag) ? "element" : comment.Tag;
            return (object)(Layout.Vertical().Gap(0)
                | Text.Block($"{comment.Number}. {comment.Comment}")
                | Text.Muted(where is not null ? $"{tag} · {where}" : $"{tag} · {comment.Selector}"));
        });

        return new Dialog(
            _ => dialogOpen.Set(false),
            new DialogHeader($"Update Plan #{plan.Id}"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | Text.P($"{pending.Count} comment(s) from the running app will be sent to the agent as a change request.")
                | (Layout.Vertical().Gap(2) | lines)
            ),
            new DialogFooter(
                new Button("Cancel").Outline().OnClick(() => dialogOpen.Set(false)),
                new Button("Update").ShortcutKey("Enter").AutoFocus().OnClick(() =>
                {
                    jobService.StartJob(new RetryPlanArgs(
                        plan.FolderPath,
                        AppPreview.FormatChangeRequest(appUrl, pending)));
                    onSubmitted();
                    dialogOpen.Set(false);
                })
            )
        ).Width(Size.Rem(32));
    }
}
