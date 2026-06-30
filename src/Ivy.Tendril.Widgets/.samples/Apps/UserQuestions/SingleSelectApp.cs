using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.UserQuestions;

[App(title: "Single Select", icon: Icons.ListChecks, group: ["UserQuestionViewer"])]
class SingleSelectApp : ViewBase
{
    public override object Build()
    {
        var questions = UseState(() => ImmutableList.Create(
            new UserQuestion
            {
                Header = "Auth method",
                Question = "Which authentication method should the new API use?",
                Options = new[]
                {
                    new UserChoice { Label = "OAuth 2.0", Description = "Delegated auth via an external identity provider. Best for third-party access." },
                    new UserChoice { Label = "JWT", Description = "Stateless signed tokens. Simple and scalable for first-party clients." },
                    new UserChoice { Label = "Session cookies", Description = "Server-side sessions. Easy to revoke, but stateful." },
                },
                Answer = "",
            },
            new UserQuestion
            {
                Header = "Database",
                Question = "Which database should back the primary store?",
                Options = new[]
                {
                    new UserChoice { Label = "PostgreSQL", Description = "Relational, rich querying, strong consistency." },
                    new UserChoice { Label = "SQLite", Description = "Embedded, zero-config, great for local/dev." },
                    new UserChoice { Label = "MongoDB", Description = "Document store with a flexible schema." },
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
