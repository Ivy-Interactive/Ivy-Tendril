using Ivy.Tendril.Models;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test.Services;

public class PlanValidationServiceTests : IDisposable
{
    private readonly string _tempRepoPath;

    public PlanValidationServiceTests()
    {
        _tempRepoPath = Path.Combine(Path.GetTempPath(), $"plan-validation-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempRepoPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRepoPath))
        {
            try
            {
                Directory.Delete(_tempRepoPath, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private PlanYaml CreateValidPlan()
    {
        return new PlanYaml
        {
            State = "Draft",
            Project = "TestProject",
            Title = "Test Plan",
            Level = "NiceToHave",
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow,
            Repos = new List<string> { _tempRepoPath },
            Prs = new List<string>(),
            Commits = new List<string>(),
            Verifications = new List<PlanVerificationEntry>(),
            RelatedPlans = new List<string>(),
            DependsOn = new List<string>()
        };
    }

    [Fact]
    public void Validate_AcceptsValidPlan()
    {
        var plan = CreateValidPlan();

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ThrowsForMissingState()
    {
        var plan = CreateValidPlan();
        plan.State = "";

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("state", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForMissingProject()
    {
        var plan = CreateValidPlan();
        plan.Project = "";

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("project", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForMissingTitle()
    {
        var plan = CreateValidPlan();
        plan.Title = "";

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("title", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForInvalidState()
    {
        var plan = CreateValidPlan();
        plan.State = "InvalidState";

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid state value", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsAllValidStates()
    {
        var validStates = new[] { "Draft", "Building", "Updating", "Executing", "ReadyForReview", "Failed", "Completed", "Skipped", "Blocked", "Icebox" };

        foreach (var state in validStates)
        {
            var plan = CreateValidPlan();
            plan.State = state;

            var exception = Record.Exception(() => PlanValidationService.Validate(plan));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void Validate_ThrowsForInvalidLevel()
    {
        var plan = CreateValidPlan();
        plan.Level = "InvalidLevel";

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid level value", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsAllValidLevels()
    {
        var validLevels = new[] { "Critical", "Bug", "NiceToHave", "Backlog", "Icebox" };

        foreach (var level in validLevels)
        {
            var plan = CreateValidPlan();
            plan.Level = level;

            var exception = Record.Exception(() => PlanValidationService.Validate(plan));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void Validate_ThrowsForDateTooOld()
    {
        var plan = CreateValidPlan();
        plan.Created = new DateTime(2019, 1, 1);

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("created", ex.Message);
        Assert.Contains("2020-01-01", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForDateTooFarInFuture()
    {
        var plan = CreateValidPlan();
        plan.Updated = DateTime.UtcNow.AddYears(2);

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("updated", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForEmptyRepos()
    {
        var plan = CreateValidPlan();
        plan.Repos = new List<string>();

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("repos", ex.Message);
    }

    [Fact]
    public void Validate_AllowsEmptyReposForCompletedWithPRs()
    {
        var plan = CreateValidPlan();
        plan.State = "Completed";
        plan.Repos = new List<string>();
        plan.Prs = new List<string> { "https://github.com/test/test/pull/1" };

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_AllowsEmptyReposForCompletedWithCommits()
    {
        var plan = CreateValidPlan();
        plan.State = "Completed";
        plan.Repos = new List<string>();
        plan.Commits = new List<string> { "abc1234567" };

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ThrowsForNonexistentRepoPath()
    {
        var plan = CreateValidPlan();
        plan.Repos = new List<string> { "/nonexistent/path" };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForInvalidPRUrl()
    {
        var plan = CreateValidPlan();
        plan.Prs = new List<string> { "not-a-url" };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid PR URL", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsValidHttpsPRUrl()
    {
        var plan = CreateValidPlan();
        plan.Prs = new List<string> { "https://github.com/owner/repo/pull/123" };

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ThrowsForInvalidCommitHash_TooShort()
    {
        var plan = CreateValidPlan();
        plan.Commits = new List<string> { "abc123" };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid commit hash", ex.Message);
        Assert.Contains("7-40", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForInvalidCommitHash_TooLong()
    {
        var plan = CreateValidPlan();
        plan.Commits = new List<string> { new string('a', 41) };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid commit hash", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForInvalidCommitHash_NonHex()
    {
        var plan = CreateValidPlan();
        plan.Commits = new List<string> { "xyz1234567" };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("hexadecimal", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsValidCommitHash_ShortForm()
    {
        var plan = CreateValidPlan();
        plan.Commits = new List<string> { "abc1234" };

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_AcceptsValidCommitHash_FullForm()
    {
        var plan = CreateValidPlan();
        plan.Commits = new List<string> { "abc123def456abc123def456abc123def456abc1" };

        var exception = Record.Exception(() => PlanValidationService.Validate(plan));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_ThrowsForVerificationWithEmptyName()
    {
        var plan = CreateValidPlan();
        plan.Verifications = new List<PlanVerificationEntry>
        {
            new PlanVerificationEntry { Name = "", Status = "Pending" }
        };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("empty name", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForVerificationWithEmptyStatus()
    {
        var plan = CreateValidPlan();
        plan.Verifications = new List<PlanVerificationEntry>
        {
            new PlanVerificationEntry { Name = "Build", Status = "" }
        };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("empty status", ex.Message);
    }

    [Fact]
    public void Validate_ThrowsForVerificationWithInvalidStatus()
    {
        var plan = CreateValidPlan();
        plan.Verifications = new List<PlanVerificationEntry>
        {
            new PlanVerificationEntry { Name = "Build", Status = "InvalidStatus" }
        };

        var ex = Assert.Throws<ArgumentException>(() => PlanValidationService.Validate(plan));

        Assert.Contains("Invalid status", ex.Message);
        Assert.Contains("Build", ex.Message);
    }

    [Fact]
    public void Validate_AcceptsAllValidVerificationStatuses()
    {
        var validStatuses = new[] { "Pending", "Pass", "Fail", "Skipped" };

        foreach (var status in validStatuses)
        {
            var plan = CreateValidPlan();
            plan.Verifications = new List<PlanVerificationEntry>
            {
                new PlanVerificationEntry { Name = "Build", Status = status }
            };

            var exception = Record.Exception(() => PlanValidationService.Validate(plan));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void ParseDate_AcceptsValidISO8601()
    {
        var dateString = "2026-04-18T15:29:55Z";

        var result = PlanValidationService.ParseDate(dateString, "testField");

        Assert.Equal(2026, result.Year);
        Assert.Equal(4, result.Month);
        Assert.Equal(18, result.Day);
    }

    [Fact]
    public void ParseDate_ThrowsForInvalidFormat()
    {
        var dateString = "invalid-date-string";

        var ex = Assert.Throws<ArgumentException>(() =>
            PlanValidationService.ParseDate(dateString, "testField"));

        Assert.Contains("Invalid date format", ex.Message);
        Assert.Contains("ISO 8601", ex.Message);
    }

    [Fact]
    public void ParseDate_ThrowsForEmptyString()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            PlanValidationService.ParseDate("", "testField"));

        Assert.Contains("Invalid date format", ex.Message);
    }
}
