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

// Disambiguate from Ivy.ContentInput (Framework widget); the enclosing Ivy namespace would
// otherwise win over the Ivy.Tendril.Widgets import for the unqualified name.
using ContentInput = Ivy.Tendril.Widgets.ContentInput;

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
        var view = new ContentInput
        {
            OnUploadFile = async e => { await Task.CompletedTask; }
        }.Bind(textState);

        Assert.NotNull(view.OnUploadFile);
    }

    [Fact]
    public void ContentInput_SubmitLabel_IsSetCorrectly()
    {
        var view = new ContentInput().SubmitLabel("Submit Label");
        Assert.Equal("Submit Label", view.SubmitLabel);
    }

    [Fact]
    public void ContentInput_MenuOptions_IsSetCorrectly()
    {
        var view = new ContentInput().MenuOptions("Option 1", "Option 2");
        Assert.Equal(["Option 1", "Option 2"], view.MenuOptions);
    }
}
