namespace Ivy.Tendril.Apps.Jobs;

public class PromptSheet(string promptText) : ViewBase
{
    public override object Build()
    {
        return new Markdown($"```\n{promptText}\n```");
    }
}
