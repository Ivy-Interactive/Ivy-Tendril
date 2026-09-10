using Ivy;
using Ivy.Tendril.Widgets;
using TendrilQuestionsWidget = Ivy.Tendril.Widgets.TendrilQuestions;

namespace WidgetSamples.Apps.TendrilQuestions;

/// <summary>Every question case the plan schema allows, in the chat design.</summary>
[App(title: "Questions", icon: Icons.ListChecks, group: ["TendrilQuestions"])]
class DemoApp : ViewBase
{
    private const string SingleSelect = """
        - id: proceed
          title: How should we proceed?
          other: false
          options:
            - title: Open a PR
              description: Open a new Pull Request against development branch.
              value: pr
              recommended: true
            - title: Review the diff first
              description: Stop the process and wait for my review first.
              value: review
        """;

    private const string MultiSelect = """
        - id: checks
          header: Verification
          title: Which checks should run before the PR?
          multiple: true
          options:
            - title: Lint
              value: lint
            - title: Build
              value: build
            - title: Unit tests
              description: Runs `dotnet test` and `vitest` across the solution.
              value: test
        """;

    private const string WithOther = """
        - id: environment
          header: Deployment
          title: Which environment should we deploy to?
          description: Pick the target for the first rollout. **Staging** mirrors production data.
          options:
            - title: Staging
              value: staging
              recommended: true
            - title: Production
              value: prod
        """;

    private const string FreeText = """
        - id: notes
          title: Anything else the agent should know?
          optional: true
          description: |
            Constraints, conventions or links. Snippets are fine:

            ```csharp
            services.AddTendril();
            ```
        """;

    private const string TwoQuestions = """
        - id: naming
          title: What should the setting be called?
          options:
            - title: Dark mode
              value: dark-mode
            - title: Appearance
              value: appearance
        - id: default
          title: Default to the system theme?
          optional: true
          options:
            - title: Yes
              value: system
            - title: No, keep light
              value: light
        """;

    private const string Answered = """
        - id: proceed
          title: How should we proceed?
          options:
            - title: Open a PR
              value: pr
            - title: Review the diff first
              value: review
          answer: review
        - id: notes
          title: Anything else?
          optional: true
        - id: owner
          title: Who reviews the PR?
        """;

    private const string Legacy = """
        1. Should the toggle live in Settings or in the header?
        2. Do we persist the choice per user or per device?
        """;

    public override object Build()
    {
        var client = UseService<IClientProvider>();

        TendrilQuestionsWidget Interactive(string content, bool submit) => new TendrilQuestionsWidget()
            .Content(content)
            .ShowSubmit(submit)
            .OnAnswer(a => client.Toast($"{a.QuestionId}: [{string.Join(", ", a.Values)}]", "OnAnswer").Info())
            .OnSubmit(s => client.Toast(s.Summary, "OnSubmit").Success());

        Card Case(string title, string note, object widget) =>
            new Card(Layout.Vertical().Gap(3) | Text.Muted(note) | widget).Title(title);

        return Layout.Vertical().Gap(6).Padding(6).Width(Size.Px(720))
            | Text.H2("Questions block")
            | Text.Muted("One widget per question case. Answers report through OnAnswer; Submit sends them all with a markdown summary.")
            | Case("Single select", "Cards with descriptions and a recommended option; picking one submits nothing until Submit.", Interactive(SingleSelect, submit: true))
            | Case("Multi select", "Checkbox indicator on every card; the answer is the list of selected values.", Interactive(MultiSelect, submit: true))
            | Case("Single select with Other", "The schema allows a typed answer by default; Other opens a text field.", Interactive(WithOther, submit: true))
            | Case("Free text, optional", "No options at all, so the answer is the text; optional questions never block Submit.", Interactive(FreeText, submit: true))
            | Case("Two questions in one block", "Stacked questions share one Clear and one Submit.", Interactive(TwoQuestions, submit: true))
            | Case("Live answers, no Submit", "A plan view reports every change as it happens instead of collecting them.", Interactive(WithOther, submit: false))
            | Case("Settled answers", "Read-only: the decisions, and which questions were left to the agent.", new TendrilQuestionsWidget().Content(Answered).ReadOnly())
            | Case("Legacy plain text", "A body that is not the questions schema renders as text.", new TendrilQuestionsWidget().Content(Legacy));
    }
}
