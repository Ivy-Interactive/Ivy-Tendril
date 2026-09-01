namespace Ivy.Tendril.Apps.Settings;

/// <summary>
/// Shared markdown field builders for project settings editors (memory, skills, context).
/// Single source for sheet/blade pairs so duplicate implementations cannot drift.
/// </summary>
public static class MarkdownFieldBuilders
{
    public static Field BuildProjectMemoryContentField(IState<string> content) =>
        content.ToCodeInput("Markdown memory content (e.g. tech stack, rules, architectural conventions)...")
            .Language(Languages.Markdown)
            .Height(Size.Units(80))
            .Width(Size.Full())
            .WithField().Label("Content (Markdown)").Required();

    public static Field BuildSkillInstructionsField(IState<string> instructions) =>
        instructions.ToCodeInput("Instructions / markdown rules...")
            .Language(Languages.Markdown)
            .Height(Size.Units(50))
            .Width(Size.Full())
            .WithField().Label("Inline Instructions");

    public static Field BuildProjectContextField(IState<string> context) =>
        context.ToCodeInput("Project context or prompt for AI agents...")
            .Language(Languages.Markdown)
            .Height(Size.Units(50))
            .Width(Size.Full())
            .WithField().Label("Context / Prompt");
}
