using Ivy.Tendril.Services;

namespace Ivy.Tendril.Apps.Jobs;

public class ReportBugDialog(IState<bool> isOpen, string jobId) : ViewBase
{
    public override object Build()
    {
        var client = UseService<IClientProvider>();
        var config = UseService<IConfigService>();
        var description = UseState("");
        var isSubmitting = UseState(false);

        async Task Submit()
        {
            if (string.IsNullOrWhiteSpace(description.Value)) return;

            isSubmitting.Set(true);
            try
            {
                var service = new BugReportService(config);
                var files = service.CollectFilesForJob(jobId);
                var result = await service.SubmitReportAsync(description.Value, files);

                if (result != null)
                {
                    client.OpenUrl(result.IssueUrl);
                    isOpen.Set(false);
                }
                else
                {
                    client.Toast("Failed to submit bug report", "Error", variant: ToastVariant.Destructive);
                }
            }
            catch (Exception ex)
            {
                client.Toast(ex.Message, "Error", variant: ToastVariant.Destructive);
            }
            finally
            {
                isSubmitting.Set(false);
            }
        }

        return new Dialog(
            _ => isOpen.Set(false),
            new DialogHeader("Report Bug"),
            new DialogBody(
                Layout.Vertical().Gap(2)
                | Text.Muted("Describe the issue. Job logs will be attached to a public GitHub issue.")
                | description.ToTextareaInput()
                    .Placeholder("What went wrong?")
                    .Rows(4)
                    .Disabled(isSubmitting.Value)),
            new DialogFooter(
                new Button("Cancel").Ghost().OnClick(() => isOpen.Set(false)).Disabled(isSubmitting.Value),
                new Button("Send Report").Primary().OnClick(async () => await Submit())
                    .Disabled(isSubmitting.Value || string.IsNullOrWhiteSpace(description.Value))
                    .Loading(isSubmitting.Value))
        ).Width(Size.Rem(32));
    }
}
