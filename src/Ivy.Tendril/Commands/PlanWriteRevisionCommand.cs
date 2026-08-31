using System.ComponentModel;
using Ivy.Tendril.Helpers;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanWriteRevisionSettings : CommandSettings
{
    [Description("Plan ID or folder path")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Read content from this file")]
    [CommandOption("--file|-f")]
    public string? FilePath { get; set; }

    [CommandOption("--stdin")]
    [Description("Read content from standard input")]
    public bool Stdin { get; set; }

    [Description("Suppress the duplicate-candidate warning (for scripted and test use)")]
    [CommandOption("--no-duplicate-check")]
    public bool NoDuplicateCheck { get; set; }

    [Description("Skip validation of questions blocks (for scripted and test use)")]
    [CommandOption("--no-question-check")]
    public bool NoQuestionCheck { get; set; }

    public int SourceCount => CliValidation.CountSources(Stdin, FilePath, "");

    public override Spectre.Console.ValidationResult Validate()
    {
        var sourceValidation = CliValidation.ValidateSingleSource(SourceCount, "--file or --stdin");
        if (!sourceValidation.Successful)
            return sourceValidation;

        return CliValidation.RequireNonEmpty(PlanId, "plan-id");
    }
}

public class PlanWriteRevisionCommand : Command<PlanWriteRevisionSettings>
{
    protected override int Execute(CommandContext context, PlanWriteRevisionSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);

        var content = ConsoleHelper.ResolveInput(settings.Stdin, settings.FilePath, null);
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("No content provided (use --file or --stdin)");

        string filePath;
        IReadOnlyList<QuestionIssue> questionWarnings;
        try
        {
            filePath = RevisionWriter.WriteNext(planFolder, content, new ConfigService(),
                out questionWarnings, !settings.NoQuestionCheck);
        }
        catch (QuestionValidationException ex)
        {
            // Nothing was written, so the agent can fix the source and retry without re-sending it.
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.Write(filePath);

        WarnAboutQuestionBlocks(questionWarnings);

        if (!settings.NoDuplicateCheck)
            WarnAboutDuplicateCandidates(planFolder);

        return 0;
    }

    /// <summary>
    ///     Prints non-blocking question-block warnings to stderr. Like the duplicate warning below,
    ///     the exit code stays 0 — a pre-schema free-text block is legal and must not fail a write.
    /// </summary>
    private static void WarnAboutQuestionBlocks(IReadOnlyList<QuestionIssue> warnings)
    {
        if (warnings.Count == 0)
            return;

        Console.Error.WriteLine();
        foreach (var warning in warnings)
            Console.Error.WriteLine($"warning: {warning}");
    }

    /// <summary>
    ///     Prints a duplicate-candidate warning to stderr after the revision has been written.
    ///     <para>
    ///         This is the only point in the CreatePlan lifecycle that runs after research, so it is
    ///         the one that catches a sibling plan created while this plan was being written: of the
    ///         four plans that collapsed onto one deliverable, 00065 was finalized with all three
    ///         already on disk and 00064 with two, while a check at creation time alone still misses
    ///         00063 (created 24 seconds before 00064).
    ///     </para>
    ///     <para>
    ///         stderr, and the exit code stays 0: the revision must still be written. A false
    ///         positive that blocks plan creation is worse than the duplicate it prevents.
    ///     </para>
    /// </summary>
    private static void WarnAboutDuplicateCandidates(string planFolder)
    {
        try
        {
            var plan = PlanCommandHelpers.ReadPlan(planFolder);
            if (string.IsNullOrWhiteSpace(plan.Title) || string.IsNullOrWhiteSpace(plan.Project))
                return;

            var plansDir = Path.GetDirectoryName(planFolder);
            if (string.IsNullOrEmpty(plansDir))
                return;

            var candidates = DuplicateCandidateFinder.Find(
                plansDir,
                plan.Title,
                plan.Project,
                PathHelper.GetFileNameCrossPlatform(planFolder));

            if (candidates.Count == 0)
                return;

            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"warning: {candidates.Count} possible duplicate plan(s) found. Review before this plan is executed:");
            Console.Error.WriteLine(DuplicateCandidateFinder.FormatBlock(candidates));
        }
        catch
        {
            // Advisory only. A plan.yaml that cannot be read must not fail a revision that is
            // already on disk.
        }
    }
}
