using System.Collections.Immutable;
using Ivy;

namespace Ivy.Tendril.Widgets;

/// <summary>
/// Renders a batch of <see cref="UserQuestion"/>s (à la Claude Code's AskUserQuestion tool)
/// as a compact tab strip — one tab per question, titled by its <see cref="UserQuestion.Header"/>
/// and badged with a check once answered. Selecting an option writes the answer back into the
/// bound <c>questions</c> state via the question's <see cref="UserQuestion.Answer"/> field, so the
/// owning view can read the answers straight off the same state it passed in.
///
/// Single-select questions keep one answer; multi-select questions store every chosen label
/// joined by <see cref="AnswerSeparator"/>. When a selected option carries a markdown
/// <see cref="UserChoice.Preview"/>, it is rendered below the choices.
/// </summary>
public class UserQuestionViewer(IState<ImmutableList<UserQuestion>> questions) : ViewBase
{
    /// <summary>Separator used to pack multiple multi-select answers into the single Answer string.</summary>
    public const string AnswerSeparator = ", ";

    public override object Build()
    {
        var list = questions.Value;

        if (list.IsEmpty)
            return Layout.Vertical().Padding(4)
                   | Text.Muted("No questions to answer.");

        var tabs = list.Select((q, i) => BuildTab(q, i)).ToArray();

        return Layout.Tabs(tabs)
            .Variant(TabsVariant.Tabs)
            .Width(Size.Full());
    }

    Tab BuildTab(UserQuestion q, int index)
    {
        var selected = SplitAnswer(q.Answer);

        var optionRows = q.Options.Select(opt => RenderOption(index, opt, selected.Contains(opt.Label)));

        var previewOpt = q.Options.FirstOrDefault(
            o => selected.Contains(o.Label) && !string.IsNullOrWhiteSpace(o.Preview));

        object? previewBlock = previewOpt is null
            ? null
            : Layout.Vertical().Gap(1)
              | Text.Muted("Preview").Small()
              | new Markdown(previewOpt.Preview!).Article();

        var body = Layout.Vertical().Gap(3)
                   | (q.MultiSelect ? (object?)Text.Muted("Select all that apply").Small() : null)
                   | Text.Block(q.Question).Bold()
                   | (Layout.Vertical().Gap(2) | optionRows)
                   | previewBlock;

        var tab = new Tab(q.Header, body);
        return string.IsNullOrWhiteSpace(q.Answer) ? tab : tab.Badge("✓");
    }

    object RenderOption(int index, UserChoice opt, bool isSelected)
    {
        var content = Layout.Vertical().Gap(1)
                      | (Text.Block((isSelected ? "● " : "○ ") + opt.Label).Bold())
                      | Text.Muted(opt.Description).Small();

        return new Card().Content(content).OnClick(() => Toggle(index, opt.Label));
    }

    void Toggle(int index, string label)
    {
        var list = questions.Value;
        if (index < 0 || index >= list.Count)
            return;

        var q = list[index];
        var selected = SplitAnswer(q.Answer).ToList();

        if (q.MultiSelect)
        {
            // Remove returns true when the label was already selected → acts as a toggle.
            if (!selected.Remove(label))
                selected.Add(label);
        }
        else
        {
            selected = selected.Contains(label) ? new List<string>() : new List<string> { label };
        }

        questions.Set(list.SetItem(index, q with { Answer = string.Join(AnswerSeparator, selected) }));
    }

    static IReadOnlyList<string> SplitAnswer(string? answer) =>
        string.IsNullOrWhiteSpace(answer)
            ? Array.Empty<string>()
            : answer.Split(AnswerSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
