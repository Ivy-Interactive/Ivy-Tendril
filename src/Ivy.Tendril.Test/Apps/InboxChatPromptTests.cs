using Ivy.Tendril.Apps.Inbox;
using Ivy.Tendril.Services.Git;

namespace Ivy.Tendril.Test.Apps;

public class InboxChatPromptTests
{
    private static GitHubIssue Issue(
        int number = 4766,
        string title = "Inbox needs an Open Chat action",
        string? body = "The action bar only fires issues off as plans.",
        string[]? labels = null,
        string[]? assignees = null,
        string? repository = "owner/repo",
        string? url = "https://github.com/owner/repo/issues/4766") =>
        new(number, title, body, labels ?? ["bug"], assignees ?? ["octocat"], repository, url);

    [Fact]
    public void Build_SingleIssue_RendersHeadingUrlLabelsAssigneesAndBody()
    {
        var prompt = InboxChatPrompt.Build([Issue()]);

        Assert.Contains("## owner/repo#4766: Inbox needs an Open Chat action", prompt);
        Assert.Contains("URL: https://github.com/owner/repo/issues/4766", prompt);
        Assert.Contains("Labels: bug", prompt);
        Assert.Contains("Assignees: octocat", prompt);
        Assert.Contains("The action bar only fires issues off as plans.", prompt);
    }

    [Fact]
    public void Build_LeadLine_PluralizesAndKeepsListOrder()
    {
        var single = InboxChatPrompt.Build([Issue(number: 1)]);
        Assert.StartsWith("Let's discuss 1 GitHub issue I selected in the Tendril Inbox.", single);

        var many = InboxChatPrompt.Build([Issue(number: 3), Issue(number: 1), Issue(number: 2)]);
        Assert.StartsWith("Let's discuss 3 GitHub issues I selected in the Tendril Inbox.", many);

        var first = many.IndexOf("#3:", StringComparison.Ordinal);
        var second = many.IndexOf("#1:", StringComparison.Ordinal);
        var third = many.IndexOf("#2:", StringComparison.Ordinal);
        Assert.True(first < second && second < third, "Issues should be rendered in list order");
    }

    [Fact]
    public void Build_DerivesUrlFromRepositoryWhenUrlIsNull()
    {
        var prompt = InboxChatPrompt.Build([Issue(number: 12, repository: "acme/widgets", url: null)]);

        Assert.Contains("URL: https://github.com/acme/widgets/issues/12", prompt);
    }

    [Fact]
    public void Build_OmitsUrlLineWhenUrlAndRepositoryAreNull()
    {
        var prompt = InboxChatPrompt.Build([Issue(number: 12, repository: null, url: null)]);

        Assert.Contains("## #12: Inbox needs an Open Chat action", prompt);
        Assert.DoesNotContain("URL:", prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_EmptyBody_RendersNoDescriptionPlaceholder(string? body)
    {
        var prompt = InboxChatPrompt.Build([Issue(body: body)]);

        Assert.Contains("No description provided.", prompt);
    }

    [Fact]
    public void Build_TruncatesLongBodyToBodyPreviewLength()
    {
        var longBody = new string('a', 600);
        var prompt = InboxChatPrompt.Build([Issue(body: longBody)]);

        Assert.Contains(InboxApp.TruncateBody(longBody, InboxChatPrompt.BodyPreviewLength), prompt);
        Assert.DoesNotContain(longBody, prompt);
        Assert.Contains(new string('a', InboxChatPrompt.BodyPreviewLength) + "…", prompt);
    }

    [Fact]
    public void Build_OmitsLabelsAndAssigneesWhenEmptyOrWhitespace()
    {
        var empty = InboxChatPrompt.Build([Issue(labels: [], assignees: [])]);
        Assert.DoesNotContain("Labels:", empty);
        Assert.DoesNotContain("Assignees:", empty);

        var whitespace = InboxChatPrompt.Build([Issue(labels: ["  "], assignees: ["", " "])]);
        Assert.DoesNotContain("Labels:", whitespace);
        Assert.DoesNotContain("Assignees:", whitespace);
    }

    [Fact]
    public void Build_CapsDetailedIssuesAndSummarizesTheRest()
    {
        var issues = Enumerable.Range(1, InboxChatPrompt.MaxDetailedIssues + 5)
            .Select(n => Issue(number: n, url: null))
            .ToList();

        var prompt = InboxChatPrompt.Build(issues);

        var headings = prompt.Split("\n").Count(l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(InboxChatPrompt.MaxDetailedIssues, headings);
        Assert.Contains("Plus 5 more selected issues, which you can fetch with gh issue view.", prompt);
    }

    [Fact]
    public void Build_EmptySelection_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, InboxChatPrompt.Build([]));
    }

    [Fact]
    public void Title_DependsOnSelectionSize()
    {
        Assert.Null(InboxChatPrompt.Title([]));
        Assert.Equal("#4766", InboxChatPrompt.Title([Issue()]));
        Assert.Equal("3 issues", InboxChatPrompt.Title([Issue(number: 1), Issue(number: 2), Issue(number: 3)]));
    }

    [Fact]
    public void ResolveIssueUrl_PrefersExplicitUrlThenDerivesThenReturnsNull()
    {
        Assert.Equal(
            "https://github.com/owner/repo/issues/4766",
            InboxApp.ResolveIssueUrl(Issue()));

        Assert.Equal(
            "https://github.com/acme/widgets/issues/12",
            InboxApp.ResolveIssueUrl(Issue(number: 12, repository: "acme/widgets", url: null)));

        Assert.Null(InboxApp.ResolveIssueUrl(Issue(repository: null, url: null)));
    }

    [Fact]
    public void ResolveIssueUrl_TreatsBlankUrlAndRepositoryAsAbsent()
    {
        Assert.Equal(
            "https://github.com/acme/widgets/issues/12",
            InboxApp.ResolveIssueUrl(Issue(number: 12, repository: "acme/widgets", url: "")));

        Assert.Equal(
            "https://github.com/acme/widgets/issues/12",
            InboxApp.ResolveIssueUrl(Issue(number: 12, repository: "acme/widgets", url: "   ")));

        Assert.Null(InboxApp.ResolveIssueUrl(Issue(repository: "", url: "")));
        Assert.Null(InboxApp.ResolveIssueUrl(Issue(repository: "   ", url: "")));
        Assert.Null(InboxApp.ResolveIssueUrl(Issue(repository: null, url: "")));
    }

    [Fact]
    public void Build_BlankUrl_DerivesUrlLineInsteadOfEmittingABareLabel()
    {
        var prompt = InboxChatPrompt.Build([Issue(url: "")]);

        Assert.Contains("URL: https://github.com/owner/repo/issues/4766", prompt);
        Assert.DoesNotContain("URL: \n", prompt);
    }
}
