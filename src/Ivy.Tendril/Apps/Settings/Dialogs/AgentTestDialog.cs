using Ivy.Tendril.Agents.Abstractions;

namespace Ivy.Tendril.Apps.Settings.Dialogs;

public enum TestStatus { Pending, Running, Passed, Failed, Warning }

public record AgentTestResult(string Label, TestStatus Status, string? Message = null, string? RawOutput = null);

public record TestModelEntry(string? Id, string DisplayName);

internal record AgentTestRow(object Icon, string Test, object? Result);

public class AgentTestDialog(
    IState<bool> isOpen,
    IState<string> selectedAgent,
    Func<List<TestModelEntry>> getModels,
    IAgentRunner runner) : ViewBase
{
    public override object? Build()
    {
        var isTesting = UseState(false);
        var testResults = UseState<List<AgentTestResult>?>(null);
        var testCts = UseState<CancellationTokenSource?>(null);
        var debugOutput = UseState<string?>(null);
        var wasOpen = UseState(false);
        var lastAgentId = UseState(selectedAgent.Value);

        if (lastAgentId.Value != selectedAgent.Value)
        {
            testResults.Set(null);
            lastAgentId.Set(selectedAgent.Value);
        }

        if (!isOpen.Value)
        {
            if (wasOpen.Value)
                wasOpen.Set(false);
            return null;
        }

        if (!wasOpen.Value)
        {
            wasOpen.Set(true);
            testResults.Set(null);
            debugOutput.Set(null);
            _ = RunTestsAsync();
        }

        async Task RunTestsAsync()
        {
            var cts = new CancellationTokenSource();
            testCts.Set(cts);
            isTesting.Set(true);
            debugOutput.Set(null);

            var agentId = selectedAgent.Value;
            var models = getModels();

            var results = new List<AgentTestResult>
            {
                new("Installation", TestStatus.Pending),
                new("Authentication", TestStatus.Pending),
            };
            results.AddRange(models.Select(m => new AgentTestResult($"Model: {m.DisplayName}", TestStatus.Pending)));
            testResults.Set([..results]);

            try
            {
                var ct = cts.Token;
                var healthCheck = runner.GetHealthCheck(agentId);

                results[0] = results[0] with { Status = TestStatus.Running };
                testResults.Set([..results]);

                var installStatus = await healthCheck.CheckInstallAsync(ct);
                results[0] = installStatus.IsInstalled
                    ? new AgentTestResult("Installation", TestStatus.Passed,
                        installStatus.Version != null ? $"v{installStatus.Version}" : "Installed")
                    : new AgentTestResult("Installation", TestStatus.Failed,
                        installStatus.Error ?? "Not installed", installStatus.Error);
                testResults.Set([..results]);

                if (!installStatus.IsInstalled)
                {
                    isTesting.Set(false);
                    testCts.Set(null);
                    return;
                }

                ct.ThrowIfCancellationRequested();

                results[1] = results[1] with { Status = TestStatus.Running };
                testResults.Set([..results]);

                var authResult = await healthCheck.CheckAuthAsync(ct);
                var providerLabel = authResult.Provider != null ? $" ({authResult.Provider})" : "";
                results[1] = authResult.Status switch
                {
                    AuthStatus.Authenticated => new AgentTestResult("Authentication", TestStatus.Passed,
                        $"Authenticated{providerLabel}"),
                    AuthStatus.NotAuthenticated => new AgentTestResult("Authentication", TestStatus.Failed,
                        "Not authenticated", authResult.Error),
                    _ => new AgentTestResult("Authentication", TestStatus.Warning,
                        authResult.Error ?? "Check inconclusive", authResult.Error)
                };
                testResults.Set([..results]);

                for (var i = 0; i < models.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var idx = 2 + i;
                    var entry = models[i];

                    results[idx] = results[idx] with { Status = TestStatus.Running };
                    testResults.Set([..results]);

                    var modelResult = await healthCheck.ValidateModelAsync(entry.Id ?? "", ct);
                    results[idx] = modelResult.Status switch
                    {
                        ModelValidationStatus.Ok => new AgentTestResult($"Model: {entry.DisplayName}", TestStatus.Passed),
                        ModelValidationStatus.InvalidModel => new AgentTestResult($"Model: {entry.DisplayName}", TestStatus.Failed,
                            modelResult.ErrorMessage ?? "Invalid model", modelResult.ErrorMessage),
                        ModelValidationStatus.AuthError => new AgentTestResult($"Model: {entry.DisplayName}", TestStatus.Failed,
                            modelResult.ErrorMessage ?? "Auth error", modelResult.ErrorMessage),
                        ModelValidationStatus.Timeout => new AgentTestResult($"Model: {entry.DisplayName}", TestStatus.Warning,
                            "Timed out", modelResult.ErrorMessage),
                        _ => new AgentTestResult($"Model: {entry.DisplayName}", TestStatus.Warning,
                            modelResult.ErrorMessage ?? "Unknown", modelResult.ErrorMessage)
                    };
                    testResults.Set([..results]);
                }
            }
            catch (OperationCanceledException)
            {
                for (var i = 0; i < results.Count; i++)
                    if (results[i].Status is TestStatus.Running or TestStatus.Pending)
                        results[i] = results[i] with { Status = TestStatus.Warning, Message = "Cancelled" };
                testResults.Set([..results]);
            }
            catch (Exception ex)
            {
                results.Add(new AgentTestResult("Unexpected error", TestStatus.Failed, "Test run failed", ex.ToString()));
                testResults.Set([..results]);
            }

            isTesting.Set(false);
            testCts.Set(null);
        }

        var results = testResults.Value;
        var body = Layout.Vertical().Gap(2);

        if (results is { Count: > 0 })
        {
            var tableRows = results.Select(r => new AgentTestRow(
                Layout.Horizontal().Gap(1)
                    | (r.Status switch
                    {
                        TestStatus.Passed => Icons.CircleCheck.ToIcon().Color(Colors.Success),
                        TestStatus.Failed => Icons.CircleX.ToIcon().Color(Colors.Destructive),
                        TestStatus.Warning => Icons.Info.ToIcon().Color(Colors.Warning),
                        TestStatus.Running => Icons.LoaderCircle.ToIcon().Color(Colors.Muted).WithAnimation(AnimationType.Rotate),
                        _ => (object)Icons.CircleDashed.ToIcon().Color(Colors.Muted)
                    })
                    | (r.RawOutput != null
                        ? new Button().Icon(Icons.Bug).Outline().Small()
                            .Tooltip("Show raw output")
                            .OnClick(() => debugOutput.Set(
                                debugOutput.Value == r.RawOutput ? null : r.RawOutput))
                        : null),
                r.Label,
                r.Message != null ? Text.Muted(r.Message) : null
            )).ToList();

            var table = new TableBuilder<AgentTestRow>(tableRows)
                .Header(r => r.Icon, "")
                .Header(r => r.Test, "Test")
                .Header(r => r.Result, "Result")
                .ColumnWidth(r => r.Result, Size.Percent(60));

            body |= table;
        }

        void CloseDialog()
        {
            testCts.Value?.Cancel();
            testResults.Set(null);
            debugOutput.Set(null);
            isOpen.Set(false);
        }

        var footer = new Button(isTesting.Value ? "Cancel" : "Close").Outline().OnClick(CloseDialog);

        return Layout.Vertical()
            | new Dialog(
                _ => CloseDialog(),
                new DialogHeader("Coding Agent Test"),
                new DialogBody(body),
                new DialogFooter(footer)
            ).Width(Size.Rem(40))
            | new AgentTestDebugDialog(debugOutput);
    }
}
