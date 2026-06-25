using System.ComponentModel;
using Ivy.StackAnalyzer;
using Ivy.Tendril.Helpers;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class ProjectAnalyzerSettings : CommandSettings
{
    [Description("Folder to analyze (supports . and relative paths)")]
    [CommandArgument(0, "<folderpath>")]
    public string FolderPath { get; set; } = "";

    public override Spectre.Console.ValidationResult Validate()
    {
        return CliValidation.RequireNonEmpty(FolderPath, "folderpath");
    }
}

/// <summary>
///     Analyzes a folder with Ivy.StackAnalyzer and prints a trimmed YAML stack report.
///     The output is deliberately minimal — only the fields the SetupProject promptware
///     needs to derive a stack hash and choose verifications/review actions. Incidental
///     libraries, CI providers, hosting brands, analytics, etc. and low-confidence signals
///     are dropped so they never enter the stack hash.
/// </summary>
public sealed class ProjectAnalyzerCommand : AsyncCommand<ProjectAnalyzerSettings>
{
    // Technology categories that define a stack. Everything else is incidental noise.
    private static readonly HashSet<TechCategory> DefiningCategories =
    [
        TechCategory.Framework,
        TechCategory.Build,
        TechCategory.Styling,
        TechCategory.Orm,
        TechCategory.Database,
        TechCategory.Testing,
        TechCategory.Iac,
    ];

    protected override async Task<int> ExecuteAsync(CommandContext context, ProjectAnalyzerSettings settings, CancellationToken cancellationToken)
    {
        var path = PathHelper.ResolvePath(settings.FolderPath);
        if (!Directory.Exists(path))
        {
            AnsiConsole.MarkupLine($"[red]Folder not found:[/] {path.EscapeMarkup()}");
            return 1;
        }

        Console.Write(await AnalyzeToYamlAsync(path, cancellationToken));
        return 0;
    }

    /// <summary>
    ///     Analyzes an absolute folder path and returns the trimmed YAML stack report.
    ///     Exposed for testing; the command resolves/validates the path before calling this.
    /// </summary>
    public static async Task<string> AnalyzeToYamlAsync(string resolvedPath, CancellationToken cancellationToken = default)
    {
        var detection = await new Analyzer().AnalyzeAsync(resolvedPath, cancellationToken);

        var components = detection.Components
            .Select(ToComponentReport)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

        var infrastructure = detection.Infrastructure
            .Select(i => new InfraReport { Kind = i.Kind, Category = Slug(i.Category) })
            .ToList();

        var report = new StackReport
        {
            Languages = ProjectLanguages(detection.Languages),
            Components = components.Count > 0 ? components : null,
            Infrastructure = infrastructure.Count > 0 ? infrastructure : null,
        };

        return YamlHelper.SerializerCompact.Serialize(report);
    }

    private static ComponentReport? ToComponentReport(Ivy.StackAnalyzer.Component component)
    {
        var technologies = component.Technologies
            .Where(t => t.Confidence != Confidence.Low && DefiningCategories.Contains(t.Category))
            .Select(t => new TechReport { Name = t.Name, Category = Slug(t.Category), Confidence = Slug(t.Confidence) })
            .ToList();

        var languages = ProjectLanguages(component.Languages);

        // Drop aggregator/empty components that carry no stack signal at all.
        if (technologies.Count == 0 && languages is null)
            return null;

        return new ComponentReport
        {
            RelativePath = component.RelativePath,
            IsWorkspaceRoot = component.IsWorkspaceRoot ? true : null,
            IsAuxiliary = component.IsAuxiliary ? true : null,
            Languages = languages,
            Technologies = technologies.Count > 0 ? technologies : null,
        };
    }

    // Significant languages (code + markup), dominant first. Data/prose (JSON, Markdown) dropped.
    private static List<string>? ProjectLanguages(IReadOnlyList<LanguageStat> languages)
    {
        var list = languages
            .Where(l => l.Type is LanguageType.Programming or LanguageType.Markup)
            .OrderByDescending(l => l.Percent)
            .Select(l => l.Name)
            .ToList();
        return list.Count > 0 ? list : null;
    }

    private static string Slug(Enum value) => value.ToString().ToLowerInvariant();

    private sealed class StackReport
    {
        public List<string>? Languages { get; set; }
        public List<ComponentReport>? Components { get; set; }
        public List<InfraReport>? Infrastructure { get; set; }
    }

    private sealed class ComponentReport
    {
        public string RelativePath { get; set; } = "";
        public bool? IsWorkspaceRoot { get; set; }
        public bool? IsAuxiliary { get; set; }
        public List<string>? Languages { get; set; }
        public List<TechReport>? Technologies { get; set; }
    }

    private sealed class TechReport
    {
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Confidence { get; set; } = "";
    }

    private sealed class InfraReport
    {
        public string Kind { get; set; } = "";
        public string Category { get; set; } = "";
    }
}
