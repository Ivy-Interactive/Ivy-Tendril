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

    [Description("Why this edit was made — reported to the plan's other chat sessions")]
    [CommandOption("--reason")]
    public string? Reason { get; set; }

    [Description("Chat session making the edit, so it is not notified about its own change")]
    [CommandOption("--chat-session")]
    public string? ChatSessionId { get; set; }

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

        ReportPlanEdit(planFolder, filePath, settings);

        if (!settings.NoDuplicateCheck)
            WarnAboutDuplicateCandidates(planFolder);

        return 0;
    }

    /// <summary>
    ///     Tells the plan's other chat sessions what this revision changed and why. Runs after the
    ///     write, so a master that is down or restarting costs a warning rather than the revision.
    /// </summary>
    private static void ReportPlanEdit(string planFolder, string filePath, PlanWriteRevisionSettings settings)
    {
        try
        {
            PlanEditEventReporter.WarnAboutMissingReason(settings.Reason);

            PlanEditEventReporter.Report(
                PathHelper.GetFileNameCrossPlatform(planFolder),
                PlanEditSummary.Describe(ReadPreviousRevision(filePath), File.ReadAllText(filePath)),
                settings.Reason,
                settings.ChatSessionId,
                Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            // Advisory only, like the warnings around it: the revision is already on disk.
            Console.Error.WriteLine($"Warning: could not describe the plan edit: {ex.Message}");
        }
    }

    /// <summary>
    ///     The <c>NNN.md</c> below the revision just written, or null when this was the first one.
    ///     Numbering follows <see cref="RevisionWriter.NextRevisionNumber" />, so the predecessor of
    ///     <c>004.md</c> is <c>003.md</c>.
    /// </summary>
    private static string? ReadPreviousRevision(string filePath)
    {
        if (!int.TryParse(Path.GetFileNameWithoutExtension(filePath), out var number) || number <= 1)
            return null;

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
            return null;

        var previous = Path.Combine(directory, $"{number - 1:D3}.md");
        return File.Exists(previous) ? File.ReadAllText(previous) : null;
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
