using System;
using System.IO;
using System.Text.RegularExpressions;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Tendril.Widgets;
using Ivy.Tendril.Models;
using Ivy.Tendril.Services.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ivy.Tendril.Test.Services;

public class JobCompletionAttachmentTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly string _plansDir;
    private readonly string _promptsRoot;

    public JobCompletionAttachmentTests()
    {
        _plansDir = Path.Combine(_tempDir.Path, "Plans");
        _promptsRoot = Path.Combine(_tempDir.Path, "Prompts");
        Directory.CreateDirectory(_plansDir);
        Directory.CreateDirectory(_promptsRoot);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    private JobCompletionHandler CreateHandler()
    {
        return new JobCompletionHandler(
            configService: null,
            logger: NullLogger.Instance,
            modelPricingService: null,
            planReaderService: null,
            telemetryService: null,
            planWatcherService: null,
            promptsRoot: _promptsRoot
        );
    }



    [Fact]
    public void ContentInput_OnUploadFile_IsNotNullAfterInstantiationAndBind()
    {
        var textState = new State<string>("");
        var view = new Ivy.Tendril.Widgets.ContentInput
        {
            OnUploadFile = async e => { await Task.CompletedTask; }
        }.Bind(textState);

        Assert.NotNull(view.OnUploadFile);
    }

    [Fact]
    public void ContentInput_SubmitLabel_IsSetCorrectly()
    {
        var view = new Ivy.Tendril.Widgets.ContentInput().SubmitLabel("Submit Label");
        Assert.Equal("Submit Label", view.SubmitLabel);
    }

    [Fact]
    public void ContentInput_MenuOptions_IsSetCorrectly()
    {
        var view = new Ivy.Tendril.Widgets.ContentInput().MenuOptions("Option 1", "Option 2");
        Assert.Equal(["Option 1", "Option 2"], view.MenuOptions);
    }

    [Fact]
    public void ResolveUploadSessionId_ReturnsSessionId_ForCreatePlanArgs()
    {
        var args = new CreatePlanArgs("Test project", "Test prompt", UploadSessionId: "session-123");
        var result = JobCompletionHandler.ResolveUploadSessionId(args);
        Assert.Equal("session-123", result);
    }

    [Fact]
    public void ResolveUploadSessionId_ReturnsSessionId_ForUpdatePlanArgs()
    {
        var args = new UpdatePlanArgs("/path/to/plan", "Instructions", UploadSessionId: "session-456");
        var result = JobCompletionHandler.ResolveUploadSessionId(args);
        Assert.Equal("session-456", result);
    }

    [Fact]
    public void ResolveUploadSessionId_ReturnsNull_ForExpandPlanArgs()
    {
        var args = new ExpandPlanArgs("/path/to/plan");
        var result = JobCompletionHandler.ResolveUploadSessionId(args);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveAttachmentPlanFolder_ReturnsUpdatePlanArgsFolderPath()
    {
        var planFolder = Path.Combine(_plansDir, "00123-TestPlan");
        Directory.CreateDirectory(planFolder);

        var job = new JobItem
        {
            Id = "job-1",
            TypedArgs = new UpdatePlanArgs(planFolder, "Instructions")
        };

        var result = JobCompletionHandler.ResolveAttachmentPlanFolder(job, _plansDir);
        Assert.Equal(planFolder, result);
    }

    [Fact]
    public void ResolveAttachmentPlanFolder_ReturnsCombinedPath_ForCreatePlanArgs()
    {
        var planFolder = Path.Combine(_plansDir, "00124-TestPlan");
        Directory.CreateDirectory(planFolder);

        var job = new JobItem
        {
            Id = "job-2",
            PlanFile = "00124-TestPlan",
            TypedArgs = new CreatePlanArgs("Project", "Prompt")
        };

        var result = JobCompletionHandler.ResolveAttachmentPlanFolder(job, _plansDir);
        Assert.Equal(planFolder, result);
    }

    [Fact]
    public void ResolveAttachmentPlanFolder_ReturnsNull_WhenDirectoryDoesNotExist()
    {
        var job = new JobItem
        {
            Id = "job-3",
            TypedArgs = new UpdatePlanArgs("/nonexistent/path", "Instructions")
        };

        var result = JobCompletionHandler.ResolveAttachmentPlanFolder(job, _plansDir);
        Assert.Null(result);
    }
}
