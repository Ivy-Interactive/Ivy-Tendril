using Ivy.Tendril.Services.Plans;
using Ivy.Tendril.Widgets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class DraftAnnotationServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly DraftAnnotationService _service;

    public DraftAnnotationServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "tendril_draft_annotations_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _service = new DraftAnnotationService(NullLogger<DraftAnnotationService>.Instance);
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
    public async Task SaveAnnotationsAsync_WritesYamlFile_AndRetrievesWithAuthor()
    {
        var annotations = new List<MarkdownAnnotation>
        {
            new()
            {
                Id = "ann-1",
                StartOffset = 10,
                EndOffset = 30,
                SelectedText = "public void Execute()",
                Comment = "Should this be async?",
                Author = "Calm Niels"
            },
            new()
            {
                Id = "ann-2",
                StartOffset = 50,
                EndOffset = 70,
                SelectedText = "var result = false;",
                Comment = "Consider true as default",
                Author = "Observant Fox"
            }
        };

        await _service.SaveAnnotationsAsync(_testDir, annotations);

        var filePath = Path.Combine(_testDir, "Artifacts", "draft_annotations.yaml");
        Assert.True(File.Exists(filePath));

        var loaded = _service.GetAnnotationsForPlan(_testDir);
        Assert.Equal(2, loaded.Count);

        Assert.Equal("ann-1", loaded[0].Id);
        Assert.Equal("Calm Niels", loaded[0].Author);
        Assert.Equal("Should this be async?", loaded[0].Comment);
        Assert.Equal("public void Execute()", loaded[0].SelectedText);

        Assert.Equal("ann-2", loaded[1].Id);
        Assert.Equal("Observant Fox", loaded[1].Author);
    }

    [Fact]
    public async Task SaveAnnotationsAsync_WithEmptyList_DeletesExistingFile()
    {
        var annotations = new List<MarkdownAnnotation>
        {
            new()
            {
                Id = "ann-1",
                Comment = "Testing",
                Author = "Wise Owl"
            }
        };

        await _service.SaveAnnotationsAsync(_testDir, annotations);
        var filePath = Path.Combine(_testDir, "Artifacts", "draft_annotations.yaml");
        Assert.True(File.Exists(filePath));

        await _service.SaveAnnotationsAsync(_testDir, []);
        Assert.False(File.Exists(filePath));

        var loaded = _service.GetAnnotationsForPlan(_testDir);
        Assert.Empty(loaded);
    }

    [Fact]
    public async Task ClearAnnotationsAsync_RemovesFile()
    {
        var annotations = new List<MarkdownAnnotation>
        {
            new()
            {
                Id = "ann-1",
                Comment = "Testing",
                Author = "Wise Owl"
            }
        };

        await _service.SaveAnnotationsAsync(_testDir, annotations);
        var loadedBefore = _service.GetAnnotationsForPlan(_testDir);
        Assert.Single(loadedBefore);

        await _service.ClearAnnotationsAsync(_testDir);

        var loadedAfter = _service.GetAnnotationsForPlan(_testDir);
        Assert.Empty(loadedAfter);
    }
}
