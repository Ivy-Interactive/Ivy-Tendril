using Ivy.Tendril.Models;
using System.ComponentModel;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Helpers;
using Spectre.Console.Cli;

namespace Ivy.Tendril.Commands;

public class PlanSetSettings : CommandSettings
{
    private static readonly string[] ValidFields =
        ["state", "project", "level", "title", "created", "updated", "executionprofile", "initialprompt", "sourceurl", "priority"];

    [Description("Plan ID (e.g., 03430)")]
    [CommandArgument(0, "<plan-id>")]
    public string PlanId { get; set; } = "";

    [Description("Field name (state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, priority)")]
    [CommandArgument(1, "<field>")]
    public string Field { get; set; } = "";

    [Description("Field value")]
    [CommandArgument(2, "<value>")]
    public string Value { get; set; } = "";

    [Description("Record Completed despite a failed verification (partial delivery)")]
    [CommandOption("--allow-failed-verifications")]
    public bool AllowFailedVerifications { get; init; }

    public override Spectre.Console.ValidationResult Validate()
    {
        var required = CliValidation.Combine(
            CliValidation.RequireNonEmpty(PlanId, "plan-id"),
            CliValidation.ValidateField(Field, ValidFields));
        if (!required.Successful)
            return required;

        var field = Field.ToLower();
        if (field == "state")
            return CliValidation.ValidateOneOf(Value, "<value> for field 'state'", CliValidation.ValidStates);
        if (field == "level")
            return CliValidation.ValidateOneOf(Value, "<value> for field 'level'", CliValidation.ValidLevels);
        if (field == "executionprofile")
            return CliValidation.ValidateOneOf(Value, "<value> for field 'executionProfile'", CliValidation.ValidExecutionProfiles);

        return Spectre.Console.ValidationResult.Success();
    }
}

public class PlanSetCommand : Command<PlanSetSettings>
{
    private readonly IPlanWatcherService _planWatcher;

    public PlanSetCommand(IPlanWatcherService planWatcher)
    {
        _planWatcher = planWatcher;
    }

    protected override int Execute(CommandContext context, PlanSetSettings settings, CancellationToken cancellationToken)
    {
        var planFolder = PlanCommandHelpers.ResolvePlanFolder(settings.PlanId);
        var plan = PlanCommandHelpers.ReadPlan(planFolder);

        switch (settings.Field.ToLower())
        {
            case "state":
                ApplyState(plan, settings);
                break;
            case "project":
                plan.Project = settings.Value;
                break;
            case "level":
                plan.Level = settings.Value;
                break;
            case "title":
                plan.Title = settings.Value;
                break;
            case "created":
                plan.Created = PlanValidationService.ParseDate(settings.Value, "created");
                break;
            case "updated":
                plan.Updated = PlanValidationService.ParseDate(settings.Value, "updated");
                break;
            case "executionprofile":
                plan.ExecutionProfile = settings.Value;
                break;
            case "initialprompt":
                plan.InitialPrompt = settings.Value;
                break;
            case "sourceurl":
                plan.SourceUrl = settings.Value;
                break;
            case "priority":
                if (!int.TryParse(settings.Value, out var priority))
                    throw new ArgumentException($"Invalid priority value: {settings.Value}. Must be an integer.");
                plan.Priority = priority;
                break;
            default:
                throw new ArgumentException($"Unknown field '{settings.Field}'. Valid fields: state, project, level, title, created, updated, executionProfile, initialPrompt, sourceUrl, priority");
        }

        if (settings.Field.ToLower() != "updated")
            plan.Updated = DateTime.UtcNow;

        PlanCommandHelpers.WritePlan(planFolder, plan, _planWatcher);

        Console.WriteLine($"Set {settings.Field} = {settings.Value}");
        return 0;
    }

    /// <summary>
    ///     Applies a state change, enforcing the same rule as
    ///     <see cref="Services.Plans.PlanReaderService.TransitionState" />: Completed is refused while a
    ///     verification is in the Fail state (see plan 00090). This command writes plan.yaml directly
    ///     rather than going through the service, so the shared
    ///     <see cref="Services.Plans.PlanCompletionGuard" /> is applied here explicitly.
    /// </summary>
    /// <exception cref="PlanTransitionBlockedException">
    ///     Thrown for a blocked Completed without <c>--allow-failed-verifications</c>. Surfaces as a
    ///     non-zero exit with the exception's message, which names the escape hatch.
    /// </exception>
    private static void ApplyState(PlanYaml plan, PlanSetSettings settings)
    {
        var isCompleting = settings.Value.Equals(nameof(PlanStatus.Completed), StringComparison.OrdinalIgnoreCase);
        if (isCompleting)
        {
            var failed = PlanCompletionGuard.FailedVerificationNames(plan);
            if (failed.Count > 0)
            {
                if (!settings.AllowFailedVerifications)
                    throw new PlanTransitionBlockedException(settings.PlanId, failed);

                // Deliberate partial delivery: record it so CreatePlan's duplicate detection can see
                // that the deliverable may be missing, instead of the plan simply reading as done.
                plan.PartialDelivery = true;
                Console.WriteLine(
                    $"Warning: completing over failed verification(s) {string.Join(", ", failed)}. " +
                    "Marked partialDelivery: true.");
            }
        }

        plan.State = settings.Value;
    }
}
