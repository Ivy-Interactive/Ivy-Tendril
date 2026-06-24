using Ivy.Plugins.Inbox;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class PluginInboxTests : IDisposable
{
    private readonly string _inboxDir;

    public PluginInboxTests()
    {
        _inboxDir = Path.Combine(Path.GetTempPath(), $"ivy-inbox-test-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_inboxDir))
            Directory.Delete(_inboxDir, recursive: true);
    }

    [Fact]
    public void Add_SimpleDescription_WritesFile()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add("Fix the login bug");

        var files = Directory.GetFiles(_inboxDir, "*.md");
        Assert.Single(files);
        Assert.Equal("Fix the login bug", File.ReadAllText(files[0]));
    }

    [Fact]
    public void Add_WithProject_WritesFrontmatter()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add(new InboxItem
        {
            Description = "Fix the bug",
            Project = "Framework"
        });

        var files = Directory.GetFiles(_inboxDir, "*.md");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("---", content);
        Assert.Contains("project: Framework", content);
        Assert.Contains("Fix the bug", content);
    }

    [Fact]
    public void Add_WithAllMetadata_WritesFullFrontmatter()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add(new InboxItem
        {
            Description = "Dashboard renders slowly",
            Project = "Framework",
            SourceUrl = "https://linear.app/ivy/issue/IVY-456",
            SourceIdentifier = "IVY-456",
            Labels = ["bug", "performance"]
        });

        var files = Directory.GetFiles(_inboxDir, "*.md");
        var content = File.ReadAllText(files[0]);
        Assert.Contains("project: Framework", content);
        Assert.Contains("sourceUrl: https://linear.app/ivy/issue/IVY-456", content);
        Assert.Contains("sourceIdentifier: IVY-456", content);
        Assert.Contains("labels: [bug, performance]", content);
        Assert.Contains("Dashboard renders slowly", content);
    }

    [Fact]
    public void Add_AutoProject_OmitsFrontmatter()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add(new InboxItem { Description = "Just a task" });

        var files = Directory.GetFiles(_inboxDir, "*.md");
        var content = File.ReadAllText(files[0]);
        Assert.DoesNotContain("---", content);
        Assert.Equal("Just a task", content);
    }

    [Fact]
    public void Add_EmptyDescription_Throws()
    {
        var inbox = new PluginInbox(_inboxDir);

        Assert.Throws<ArgumentException>(() => inbox.Add(""));
        Assert.Throws<ArgumentException>(() => inbox.Add("   "));
    }

    [Fact]
    public void Add_WithSourceIdentifier_UsesItInFilename()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add(new InboxItem
        {
            Description = "Some issue",
            SourceIdentifier = "IVY-456"
        });

        var files = Directory.GetFiles(_inboxDir, "*.md");
        Assert.Single(files);
        Assert.Contains("IVY-456", Path.GetFileName(files[0]));
    }

    [Fact]
    public void Add_WithoutSourceIdentifier_UsesHashInFilename()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.Add(new InboxItem { Description = "Some task" });

        var files = Directory.GetFiles(_inboxDir, "*.md");
        var filename = Path.GetFileName(files[0]);
        // Should be timestamp-hash.md format
        Assert.Matches(@"^\d{8}T\d{6}-[a-f0-9]{8}\.md$", filename);
    }

    [Fact]
    public void AddRange_MultipleItems_WritesMultipleFiles()
    {
        var inbox = new PluginInbox(_inboxDir);

        inbox.AddRange([
            new InboxItem { Description = "Task 1", SourceIdentifier = "#1" },
            new InboxItem { Description = "Task 2", SourceIdentifier = "#2" },
            new InboxItem { Description = "Task 3", SourceIdentifier = "#3" }
        ]);

        var files = Directory.GetFiles(_inboxDir, "*.md");
        Assert.Equal(3, files.Length);
    }

    [Fact]
    public void Add_CreatesDirectoryIfMissing()
    {
        var nestedDir = Path.Combine(_inboxDir, "nested", "inbox");
        var inbox = new PluginInbox(nestedDir);

        inbox.Add("Test task");

        Assert.True(Directory.Exists(nestedDir));
        Assert.Single(Directory.GetFiles(nestedDir, "*.md"));
    }

    [Fact]
    public void FormatContent_RoundTripsWithInboxWatcher()
    {
        var item = new InboxItem
        {
            Description = "Fix the memory leak in chart rendering",
            Project = "Framework",
            SourceUrl = "https://linear.app/ivy/issue/IVY-789",
            SourceIdentifier = "IVY-789"
        };

        var content = PluginInbox.FormatContent(item);
        var parsed = InboxWatcherService.ParseContent(content);

        Assert.Equal("Framework", parsed.Project);
        Assert.Equal("Fix the memory leak in chart rendering", parsed.Description);
        Assert.Equal("https://linear.app/ivy/issue/IVY-789", parsed.SourceUrl);
        Assert.Equal("IVY-789", parsed.SourceIdentifier);
    }
}
