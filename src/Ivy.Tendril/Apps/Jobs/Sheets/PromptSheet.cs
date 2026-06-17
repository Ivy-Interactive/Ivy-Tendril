namespace Ivy.Tendril.Apps.Jobs.Sheets;

public class PromptSheet(string promptText) : ViewBase
{
    public override object Build()
    {
        return new CodeBlock(promptText, Languages.Text);
    }
}
