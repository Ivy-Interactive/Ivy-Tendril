using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class PlanDiffCommentServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly PlanDiffCommentService _service;

    public PlanDiffCommentServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "tendril_draft_diff_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new PlanDiffCommentService(NullLogger<PlanDiffCommentService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task SaveDraftCommentsAsync_WritesYamlFile_AndRetrievesWithAuthor()
    {
        var comments = new List<DraftComment>
        {
            new("src/Main.cs", "I10", "Should this handle null?", 10, "Calm Niels"),
            new("src/App.cs", "N25", "Refactor into separate class", 25, "Observant Fox")
        };

        await _service.SaveDraftCommentsAsync(_testDir, comments);

        var filePath = Path.Combine(_testDir, "Artifacts", "draft_diff_comments.yaml");
        Assert.True(File.Exists(filePath));

        var loaded = _service.GetDraftCommentsForPlan(_testDir);
        Assert.Equal(2, loaded.Count);

        Assert.Equal("src/Main.cs", loaded[0].FilePath);
        Assert.Equal("I10", loaded[0].ChangeKey);
        Assert.Equal("Should this handle null?", loaded[0].Content);
        Assert.Equal(10, loaded[0].LineNumber);
        Assert.Equal("Calm Niels", loaded[0].Author);

        Assert.Equal("src/App.cs", loaded[1].FilePath);
        Assert.Equal("Observant Fox", loaded[1].Author);
    }

    [Fact]
    public async Task SaveDraftCommentsAsync_WithEmptyList_DeletesFile()
    {
        var comments = new List<DraftComment>
        {
            new("src/Main.cs", "I10", "Testing", 10, "Wise Owl")
        };

        await _service.SaveDraftCommentsAsync(_testDir, comments);
        var filePath = Path.Combine(_testDir, "Artifacts", "draft_diff_comments.yaml");
        Assert.True(File.Exists(filePath));

        await _service.SaveDraftCommentsAsync(_testDir, []);
        Assert.False(File.Exists(filePath));

        var loaded = _service.GetDraftCommentsForPlan(_testDir);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task ClearDraftCommentsAsync_RemovesFile()
    {
        var comments = new List<DraftComment>
        {
            new("src/Main.cs", "I10", "Testing", 10, "Wise Owl")
        };

        await _service.SaveDraftCommentsAsync(_testDir, comments);
        var loadedBefore = _service.GetDraftCommentsForPlan(_testDir);
        Assert.Single(loadedBefore);

        await _service.ClearDraftCommentsAsync(_testDir);

        var loadedAfter = _service.GetDraftCommentsForPlan(_testDir);
        Assert.Empty(loadedAfter);
    }

    [Fact]
    public async Task SaveAndClearDraftComments_FiresCommentsChangedEvent()
    {
        string? firedFolder = null;
        List<DraftComment>? firedList = null;
        _service.CommentsChanged += (folder, list) =>
        {
            firedFolder = folder;
            firedList = list;
        };

        var comments = new List<DraftComment>
        {
            new("src/Main.cs", "I10", "Realtime diff test", 10, "Calm Niels")
        };

        await _service.SaveDraftCommentsAsync(_testDir, comments);
        Assert.Equal(_testDir, firedFolder);
        Assert.NotNull(firedList);
        Assert.Single(firedList!);

        await _service.ClearDraftCommentsAsync(_testDir);
        Assert.Equal(_testDir, firedFolder);
        Assert.NotNull(firedList);
        Assert.Empty(firedList!);
    }
}
