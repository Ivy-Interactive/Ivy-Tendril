namespace FieldToolsDemo;

[App(title: "Field Tools", icon: Icons.TextSelect)]
public class FieldToolsApp : ViewBase
{
    public override object? Build()
    {
        return Layout.Tabs(
            new Tab("Top", new TopSample()),
            new Tab("Left", new LeftSample())
        ).Variant(TabsVariant.Content);
    }
}

// Tools rendered on the right of the label row when LabelPosition = Top.
public class TopSample : ViewBase
{
    public override object? Build()
    {
        var name = UseState<string>();
        var description = UseState<string>();
        var priority = UseState("Normal");

        return Layout.Vertical().Gap(6).Padding(4)
            | Text.H3("LabelPosition.Top — tools on the right of the label")

            // A simple single-button tool.
            | name.ToTextInput()
                .Placeholder("Enter your name")
                .WithField()
                .Label("Name")
                .Tools(new Button("Clear", _ => name.Set("")).Variant(ButtonVariant.Ghost).Small())

            // A simple single-button tool.
            | name.ToTextInput()
                .Placeholder("Enter your name")
                .WithField()
                .Label("Name")
            
            // A priority selector with a multi-button tools slot.
            | priority.ToSelectInput(new[] { "Normal", "High", "Urgent" }.ToOptions())
                .Variant(SelectInputVariant.Toggle)
                .WithField()
                .Label("Priority")
                .Tools(
                    Layout.Horizontal().Gap(1)
                        | new Button("Reset", _ => priority.Set("Normal")).Small()
                        | new Button(null, icon: Icons.Star).Variant(ButtonVariant.Ghost).Small()
                )

            // A deliberately tall tools slot to prove the label->input padding stays constant.
            | description.ToTextareaInput()
                .Placeholder("Enter task description...")
                .WithField()
                .Label("Describe the task for the new plan")
                .Required()
                .Tools(
                    new Card(
                        Layout.Vertical().Gap(1)
                            | Text.Muted("Tall tools block")
                            | new Button("A").Variant(ButtonVariant.Ghost).Small()
                            | new Button("B").Variant(ButtonVariant.Ghost).Small()
                    )
                );
    }
}

// Tools rendered below the label when LabelPosition = Left.
public class LeftSample : ViewBase
{
    public override object? Build()
    {
        var email = UseState<string>();
        var notes = UseState<string>();

        return Layout.Vertical().Gap(6).Padding(4)
            | Text.H3("LabelPosition.Left — tools below the label")

            | email.ToTextInput()
                .Variant(TextInputVariant.Email)
                .Placeholder("you@example.com")
                .WithField()
                .Label("Work Email")
                .LabelPosition(LabelPosition.Left)
                .Description("We'll send updates here")
                .Required()
                .Tools(new Button("Verify", _ => { }).Variant(ButtonVariant.Outline).Small())

            | notes.ToTextareaInput()
                .Placeholder("Notes...")
                .WithField()
                .Label("Notes")
                .LabelPosition(LabelPosition.Left)
                .Tools(
                    Layout.Vertical().Gap(1)
                        | new Button("Clear", _ => notes.Set("")).Variant(ButtonVariant.Ghost).Small()
                        | Text.Muted("2 tools")
                );
    }
}
