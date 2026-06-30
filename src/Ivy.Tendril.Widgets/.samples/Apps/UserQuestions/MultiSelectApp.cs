using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.UserQuestions;

[App(title: "Multi Select", icon: Icons.CheckCheck, group: ["UserQuestionViewer"])]
class MultiSelectApp : ViewBase
{
    public override object Build()
    {
        var questions = UseState(() => ImmutableList.Create(
            new UserQuestion
            {
                Header = "Features",
                Question = "Which features should we enable for the first release?",
                MultiSelect = true,
                Options = new[]
                {
                    new UserChoice { Label = "Dark mode", Description = "Ship a dark theme toggle in settings." },
                    new UserChoice { Label = "Offline cache", Description = "Cache the last session locally for offline reads." },
                    new UserChoice { Label = "Push notifications", Description = "Real-time alerts via the mobile apps." },
                    new UserChoice { Label = "Audit log", Description = "Record every write for compliance review." },
                },
                Answer = "",
            },
            new UserQuestion
            {
                Header = "Targets",
                Question = "Which platforms must the build target?",
                MultiSelect = true,
                Options = new[]
                {
                    new UserChoice { Label = "Windows", Description = "x64 desktop." },
                    new UserChoice { Label = "macOS", Description = "Apple silicon + Intel." },
                    new UserChoice { Label = "Linux", Description = "Ubuntu LTS." },
                },
                Answer = "",
            }
        ));

        return Layout.Horizontal().Gap(6).Padding(4).Height(Size.Full())
               | (Layout.Vertical().Width(Size.Fraction(0.6f)) | new UserQuestionViewer(questions))
               | AnswerSummary(questions);
    }

    static object AnswerSummary(IState<ImmutableList<UserQuestion>> questions) =>
        Layout.Vertical().Gap(3).Width(Size.Fraction(0.4f))
        | Text.H4("Captured answers")
        | questions.Value.Select(q => (object)(Layout.Vertical().Gap(0)
            | Text.Muted(q.Header).Small()
            | Text.Block(string.IsNullOrWhiteSpace(q.Answer) ? "—" : q.Answer)));
}
