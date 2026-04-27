using Ivy.Core;
using Ivy.Tendril.Models;
using Ivy.Tendril.Helpers;

namespace Ivy.Tendril.Views.Sheets;

public class VerificationReportSheet(
    IState<string?> openVerification,
    PlanFile? selectedPlan) : ViewBase
{
    public override object Build()
    {
        var verificationReportQuery = UseQuery<string, string>(
            openVerification.Value ?? "",
            async (name, ct) =>
            {
                if (string.IsNullOrEmpty(name) || selectedPlan is null) return "";
                var verificationDir = Path.GetFullPath(Path.Combine(selectedPlan.FolderPath, "verification"));
                var resolvedPath = Path.GetFullPath(Path.Combine(verificationDir, $"{name}.md"));
                if (!resolvedPath.StartsWith(verificationDir, StringComparison.OrdinalIgnoreCase))
                    return "Access denied: file is outside the verification folder.";
                return await Task.Run(() =>
                    File.Exists(resolvedPath) ? FileHelper.ReadAllText(resolvedPath) : $"No report found for {name}.", ct);
            },
            initialValue: ""
        );

        if (openVerification.Value is not { } verName)
            return new Empty();

        return new Sheet(
            () => openVerification.Set(null),
            verificationReportQuery.Loading
                ? Text.Muted("Loading...")
                : verificationReportQuery.Error is { } err
                    ? Text.Muted($"Failed to load verification report: {err.Message}")
                    : new Markdown(verificationReportQuery.Value).DangerouslyAllowLocalFiles(),
            verName
        ).Width(Size.Half()).Resizable();
    }
}
