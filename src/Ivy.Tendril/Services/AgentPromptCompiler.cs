using System.Reflection;

namespace Ivy.Tendril.Services;

public static class AgentPromptCompiler
{
    private static readonly Lazy<string?> Template = new(() =>
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("Ivy.Tendril.Prompts.AgentPrompt.md");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string? Compile(IConfigService config)
    {
        var template = Template.Value;
        if (template == null) return null;

        var tendrilHome = config.TendrilHome.Replace('\\', '/');
        var planFolder = config.PlanFolder.Replace('\\', '/');

        return template
            .Replace("{TENDRIL_HOME}", tendrilHome)
            .Replace("{PLAN_FOLDER}", planFolder);
    }
}
