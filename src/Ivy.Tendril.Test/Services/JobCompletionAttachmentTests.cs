using System;
using System.IO;
using System.Text.RegularExpressions;
using Ivy;
using Ivy.Core.Hooks;
using Ivy.Widgets.ContentInputView;
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
            worktreeLifecycleLogger: null,
            promptsRoot: _promptsRoot
        );
    }

    [Fact]
    public void RelocateUpcomingAttachments_WithSessionId_MovesFilesAndUpdatesReferences()
    {
        // Arrange
        var handler = CreateHandler();
        var sessionId = "session_success_123";
        var planFolderName = "00001-TestPlan";
        var planFolder = Path.Combine(_plansDir, planFolderName);
        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "Revisions"));

        var sessionDir = Path.Combine(_plansDir, ".upcoming-attachments", sessionId);
        Directory.CreateDirectory(sessionDir);

        var tempFile1 = Path.Combine(sessionDir, "screenshot.png");
        var tempFile2 = Path.Combine(sessionDir, "notes.txt");
        File.WriteAllText(tempFile1, "fake png content");
        File.WriteAllText(tempFile2, "fake text content");

        // Write references in plan files using the actual temporary paths
        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        var planYamlContent = $@"
initialPrompt: Please check this [file: {tempFile1}] and details in [file: {tempFile2}]
state: Draft
";
        File.WriteAllText(planYamlPath, planYamlContent);

        var revisionPath = Path.Combine(planFolder, "Revisions", "00001-001.md");
        var revisionContent = $@"# Revision 1
Here is the image: ![image](file://{tempFile1})
and the text: [notes](file://{tempFile2})
";
        File.WriteAllText(revisionPath, revisionContent);

        var args = new CreatePlanArgs(
            Description: $"[file: {tempFile1}] [file: {tempFile2}]",
            Project: "Framework",
            UploadSessionId: sessionId
        );
        var job = new JobItem
        {
            Id = "job-1",
            PlanFile = planFolderName,
            TypedArgs = args
        };

        // Act
        var method = typeof(JobCompletionHandler).GetMethod("RelocateUpcomingAttachments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(handler, new object[] { _plansDir, job });

        // Assert
        var targetFile1 = Path.Combine(planFolder, "attachments", "screenshot.png");
        var targetFile2 = Path.Combine(planFolder, "attachments", "notes.txt");

        // 1. Files moved to target
        Assert.True(File.Exists(targetFile1));
        Assert.True(File.Exists(targetFile2));
        Assert.Equal("fake png content", File.ReadAllText(targetFile1));
        Assert.Equal("fake text content", File.ReadAllText(targetFile2));

        // 2. Temp directories deleted
        Assert.False(Directory.Exists(sessionDir));
        Assert.False(Directory.Exists(Path.Combine(_plansDir, ".upcoming-attachments")));

        // 3. References updated in plan.yaml
        var updatedYaml = File.ReadAllText(planYamlPath);
        Assert.Contains($"[file: {targetFile1}]", updatedYaml);
        Assert.Contains($"[file: {targetFile2}]", updatedYaml);
        Assert.DoesNotContain(tempFile1, updatedYaml);

        // 4. References updated in revisions
        var updatedRevision = File.ReadAllText(revisionPath);
        Assert.Contains($"file://{targetFile1}", updatedRevision);
        Assert.Contains($"file://{targetFile2}", updatedRevision);
        Assert.DoesNotContain(tempFile1, updatedRevision);
    }

    [Fact]
    public void CleanupUpcomingAttachments_WithSessionId_DeletesFolderAndParentIfEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var sessionId = "session_fail_456";
        var sessionDir = Path.Combine(_plansDir, ".upcoming-attachments", sessionId);
        Directory.CreateDirectory(sessionDir);
        File.WriteAllText(Path.Combine(sessionDir, "leftover.png"), "unsubmitted content");

        var args = new CreatePlanArgs(
            Description: "Some plan text",
            Project: "Framework",
            UploadSessionId: sessionId
        );
        var job = new JobItem
        {
            Id = "job-2",
            TypedArgs = args
        };

        // Act
        var method = typeof(JobCompletionHandler).GetMethod("CleanupUpcomingAttachments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(handler, new object[] { _plansDir, job });

        // Assert
        Assert.False(Directory.Exists(sessionDir));
        Assert.False(Directory.Exists(Path.Combine(_plansDir, ".upcoming-attachments")));
    }

    [Fact]
    public void RelocateUpcomingAttachments_FallbackRegex_MovesFilesAndCleansUp()
    {
        // Arrange
        var handler = CreateHandler();
        var planFolderName = "00002-FallbackPlan";
        var planFolder = Path.Combine(_plansDir, planFolderName);
        Directory.CreateDirectory(planFolder);
        Directory.CreateDirectory(Path.Combine(planFolder, "Revisions"));

        var sessionDir = Path.Combine(_plansDir, ".upcoming-attachments", "fallback_789");
        Directory.CreateDirectory(sessionDir);

        var tempFile = Path.Combine(sessionDir, "screenshot.png");
        File.WriteAllText(tempFile, "fallback png content");

        var planYamlPath = Path.Combine(planFolder, "plan.yaml");
        var planYamlContent = $"initialPrompt: [file: {tempFile}]";
        File.WriteAllText(planYamlPath, planYamlContent);

        // Session ID is intentionally left null to test the regex fallback route
        var args = new CreatePlanArgs(
            Description: $"Plan text referencing [file: {tempFile}]",
            Project: "Framework",
            UploadSessionId: null
        );
        var job = new JobItem
        {
            Id = "job-3",
            PlanFile = planFolderName,
            TypedArgs = args
        };

        // Act
        var method = typeof(JobCompletionHandler).GetMethod("RelocateUpcomingAttachments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(handler, new object[] { _plansDir, job });

        // Assert
        var targetFile = Path.Combine(planFolder, "attachments", "screenshot.png");
        Assert.True(File.Exists(targetFile));
        Assert.Equal("fallback png content", File.ReadAllText(targetFile));

        Assert.False(Directory.Exists(sessionDir));
        Assert.False(Directory.Exists(Path.Combine(_plansDir, ".upcoming-attachments")));

        var updatedYaml = File.ReadAllText(planYamlPath);
        Assert.Contains($"[file: {targetFile}]", updatedYaml);
        Assert.DoesNotContain(tempFile, updatedYaml);
    }

    [Fact]
    public void CleanupUpcomingAttachments_FallbackRegex_DeletesFolderAndParentIfEmpty()
    {
        // Arrange
        var handler = CreateHandler();
        var sessionDir = Path.Combine(_plansDir, ".upcoming-attachments", "fallback_012");
        Directory.CreateDirectory(sessionDir);
        var tempFile = Path.Combine(sessionDir, "leftover.png");
        File.WriteAllText(tempFile, "unsubmitted content");

        // Session ID is null, description references tempFile
        var args = new CreatePlanArgs(
            Description: $"Plan text referencing [file: {tempFile}]",
            Project: "Framework",
            UploadSessionId: null
        );
        var job = new JobItem
        {
            Id = "job-4",
            TypedArgs = args
        };

        // Act
        var method = typeof(JobCompletionHandler).GetMethod("CleanupUpcomingAttachments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        method.Invoke(handler, new object[] { _plansDir, job });

        // Assert
        Assert.False(Directory.Exists(sessionDir));
        Assert.False(Directory.Exists(Path.Combine(_plansDir, ".upcoming-attachments")));
    }

    [Fact]
    public void ContentInputView_OnUploadFile_IsNotNullAfterInstantiationAndBind()
    {
        var textState = new State<string>("");
        var view = new ContentInputView
        {
            OnUploadFile = async e => { await Task.CompletedTask; }
        }.Bind(textState);

        Assert.NotNull(view.OnUploadFile);
    }
}
