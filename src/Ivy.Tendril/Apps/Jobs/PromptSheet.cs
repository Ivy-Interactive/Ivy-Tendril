namespace Ivy.Tendril.Apps.Jobs;

public class PromptSheet(string promptText) : ViewBase
{
    private readonly string _promptText = promptText;

    public override object Build()
    {
        return new Markdown($"```\n{_promptText}\n```");
    }
}
