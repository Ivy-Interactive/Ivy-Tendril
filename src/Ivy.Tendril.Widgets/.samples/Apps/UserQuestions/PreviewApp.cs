using System.Collections.Immutable;
using Ivy;
using Ivy.Tendril.Widgets;

namespace WidgetSamples.Apps.UserQuestions;

[App(title: "With Previews", icon: Icons.Eye, group: ["UserQuestionViewer"])]
class PreviewApp : ViewBase
{
    public override object Build()
    {
        var questions = UseState(() => ImmutableList.Create(
            new UserQuestion
            {
                Header = "Layout",
                Question = "Which navigation layout should the dashboard use?",
                Options = new[]
                {
                    new UserChoice
                    {
                        Label = "Left sidebar",
                        Description = "Persistent vertical nav on the left.",
                        Preview = """
                            ```
                            +-------+---------------------+
                            | Nav   |  Content            |
                            | • Home|                     |
                            | • Plans                     |
                            | • Jobs|                     |
                            +-------+---------------------+
                            ```
                            """,
                    },
                    new UserChoice
                    {
                        Label = "Top bar",
                        Description = "Horizontal nav across the top.",
                        Preview = """
                            ```
                            +-----------------------------+
                            | Home   Plans   Jobs         |
                            +-----------------------------+
                            |  Content                    |
                            |                             |
                            +-----------------------------+
                            ```
                            """,
                    },
                },
                Answer = "",
            },
            new UserQuestion
            {
                Header = "Error API",
                Question = "How should the client surface API errors?",
                Options = new[]
                {
                    new UserChoice
                    {
                        Label = "Result type",
                        Description = "Return a discriminated result; no exceptions for expected failures.",
                        Preview = """
                            ```csharp
                            var result = await api.LoadAsync(id);
                            if (result.IsError)
                                ShowToast(result.Error);
                            else
                                Render(result.Value);
                            ```
                            """,
                    },
                    new UserChoice
                    {
                        Label = "Exceptions",
                        Description = "Throw typed exceptions and handle them centrally.",
                        Preview = """
                            ```csharp
                            try
                            {
                                Render(await api.LoadAsync(id));
                            }
                            catch (ApiException ex)
                            {
                                ShowToast(ex.Message);
                            }
                            ```
                            """,
                    },
                },
                Answer = "",
            }
        ));

        return Layout.Vertical().Gap(4).Padding(4).Width(Size.Fraction(0.7f))
               | new UserQuestionViewer(questions);
    }
}
