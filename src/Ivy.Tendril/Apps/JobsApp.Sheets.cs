using Ivy.Tendril.Apps.Plans;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps;

public partial class JobsApp
{
    private object RenderWithSheets(
        object dataTable,
        IState<string?> showPlan,
        IState<string?> showOutput,
        IState<string?> showPrompt,
        IPlanReaderService planService,
        IConfigService config,
        IState<string?> openFile,
        IJobService jobService,
        IStream<string> outputStream,
        IState<bool> hasStreamContent,
        object layout)
    {
        if (showPlan.Value is { } planPath)
        {
            var planSheetView = new PlanSheet(planPath, planService, config, openFile);
            var fileLinkSheet = planSheetView.BuildFileLinkSheet();

            var planSheet = new Sheet(
                () => showPlan.Set(null),
                planSheetView.Build(),
                planSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();

            if (fileLinkSheet is not null) return layout | new Fragment(dataTable, planSheet, fileLinkSheet);

            return layout | new Fragment(dataTable, planSheet);
        }

        if (showOutput.Value is { } jobId)
        {
            var outputSheetView = new OutputSheet(jobId, jobService, outputStream, hasStreamContent);

            var outputSheet = new Sheet(
                () => showOutput.Set(null),
                outputSheetView.Build(),
                outputSheetView.GetSheetTitle()
            ).Width(Size.Half()).Resizable();

            return layout | new Fragment(dataTable, outputSheet);
        }

        if (showPrompt.Value is { } promptText)
        {
            var promptSheetView = new PromptSheet(promptText);

            var promptSheet = new Sheet(
                () => showPrompt.Set(null),
                promptSheetView.Build(),
                "Full Prompt"
            ).Width(Size.Half()).Resizable();

            return layout | new Fragment(dataTable, promptSheet);
        }

        return layout | dataTable;
    }
}
