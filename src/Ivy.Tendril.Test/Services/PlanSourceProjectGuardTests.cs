using System.Diagnostics;
using Ivy.Tendril.Services;
using Ivy.Tendril.Services.Git;
using Ivy.Tendril.Services.Plans;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ivy.Tendril.Test.Services;

public class PlanSourceProjectGuardTests
{
    [Fact]
    public void Throws_When_Issue_Repo_Not_In_Project()
    {
        var tendrilRepo = CreateTempGitRepo("https://github.com/Ivy-Interactive/Ivy-Tendril.git");
        try
        {
            var project = new ProjectConfig { Name = "Ivy-Tendril", Repos = [new RepoRef { Path = tendrilRepo }] };
            var github = NewGithub(project);

            var ex = Assert.Throws<ArgumentException>(() =>
                PlanSourceProjectGuard.EnsureSourceUrlMatchesProject(
                    "https://github.com/nielsbosma/lots-of-dev-tools/issues/22", project, github));

            Assert.Contains("lots-of-dev-tools", ex.Message);
            Assert.Contains("Ivy-Tendril", ex.Message);
        }
        finally
        {
            Directory.Delete(tendrilRepo, true);
        }
    }

    [Fact]
    public void Allows_When_Issue_Repo_In_Project()
    {
        var repo = CreateTempGitRepo("https://github.com/Ivy-Interactive/Ivy-Tendril.git");
        try
        {
            var project = new ProjectConfig { Name = "Ivy-Tendril", Repos = [new RepoRef { Path = repo }] };
            var github = NewGithub(project);

            // No throw — the issue's repo is the project's repo.
            PlanSourceProjectGuard.EnsureSourceUrlMatchesProject(
                "https://github.com/Ivy-Interactive/Ivy-Tendril/issues/5", project, github);
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/not/a/github/issue")] // not a GitHub issue/PR URL
    public void NoOp_When_No_Recognizable_Issue_Url(string? sourceUrl)
    {
        var project = new ProjectConfig { Name = "P", Repos = [new RepoRef { Path = @"C:\nope" }] };
        var github = NewGithub(project);

        // No throw regardless of project repos.
        PlanSourceProjectGuard.EnsureSourceUrlMatchesProject(sourceUrl, project, github);
    }

    [Fact]
    public void FailsOpen_When_Project_Repos_Unresolvable()
    {
        // The project's only repo path doesn't exist / has no git remote, so membership can't be
        // determined — don't block creation.
        var project = new ProjectConfig { Name = "P", Repos = [new RepoRef { Path = @"C:\nonexistent\xyz-999" }] };
        var github = NewGithub(project);

        PlanSourceProjectGuard.EnsureSourceUrlMatchesProject(
            "https://github.com/nielsbosma/lots-of-dev-tools/issues/22", project, github);
    }

    private static GithubService NewGithub(params ProjectConfig[] projects) =>
        new(new ConfigService(new TendrilSettings { Projects = projects.ToList() }), NullLogger<GithubService>.Instance);

    private static string CreateTempGitRepo(string remoteUrl)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ivy-srcguard-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        RunGit(tempDir, "init");
        RunGit(tempDir, $"remote add origin {remoteUrl}");
        return tempDir;
    }

    private static void RunGit(string workingDir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit();
    }
}
