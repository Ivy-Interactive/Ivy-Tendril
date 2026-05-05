using Ivy.Tendril.Apps.Jobs;
using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private static object RenderWithSheets(
        object dataTable,
        IState<string?> showPlan,
        IState<string?> showOutput,
        IState<string?> showPrompt,
        IPlanReaderService planService,
        IConfigService config,
        IState<string?> openFile,
        IJobService jobService,
        IWriteStream<string> outputStream,
        IState<bool> hasStreamContent,
        IState<string?> streamingJobId,
        LayoutView layout)
    {
        object? activeSheet = null;

        if (showPlan.Value is { } planPath)
        {
            var planSheetView = new PlanSheet(planPath, planService, config, openFile);
            var fileLinkSheet = planSheetView.BuildFileLinkSheet();

            var planSheet = new Sheet(
                () => showPlan.Set(null),
                planSheetView.Build(),
                planSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();

            activeSheet = fileLinkSheet is not null
                ? new Fragment(planSheet, fileLinkSheet)
                : planSheet;
        }
        else if (showOutput.Value is { } jobId)
        {
            var outputSheetView = new OutputSheet(jobId, jobService, outputStream, hasStreamContent, streamingJobId);

            activeSheet = new Sheet(
                () => showOutput.Set(null),
                outputSheetView.Build(),
                outputSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();
        }
        else if (showPrompt.Value is { } promptText)
        {
            var promptSheetView = new PromptSheet(promptText);

            activeSheet = new Sheet(
                () => showPrompt.Set(null),
                promptSheetView.Build(),
                "Full Prompt"
            ).Width(Size.Half()).Resizable();
        }

        return layout | new Fragment(dataTable, activeSheet);
    }
}
