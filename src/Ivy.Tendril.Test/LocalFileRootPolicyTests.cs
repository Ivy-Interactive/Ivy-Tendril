using Ivy.Tendril.Controllers;
using Ivy.Tendril.Services;

namespace Ivy.Tendril.Test;

public class LocalFileRootPolicyTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tendril-lfrp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // The OS temp dir itself can sit behind a symlink (e.g. macOS's /var -> /private/var).
        // Canonicalize so a raw root passed directly to TryResolve (bypassing ComputeRoots, which
        // adds the resolved form separately) still matches the resolved request path.
        return LocalFileRootPolicy.TryResolveRealPath(dir, out var real) ? real : dir;
    }

    [Fact]
    public void ComputeRoots_IncludesHomePlanFolderReposAndSecurityRoots_DropsBlankAndDuplicate()
    {
        var home = CreateTempDir();
        var repoPath = CreateTempDir();
        var extraRoot = CreateTempDir();

        var settings = new TendrilSettings
        {
            Projects = new List<ProjectConfig>
            {
                new()
                {
                    Name = "demo",
                    Repos = new List<RepoRef> { new() { Path = repoPath } }
                }
            },
            Security = new SecuritySettings
            {
                LocalFileRoots = new List<string> { extraRoot, "", "  ", extraRoot }
            }
        };

        var config = new ConfigService(settings, home);
        var roots = LocalFileRootPolicy.ComputeRoots(config);

        Assert.Contains(Path.GetFullPath(home), roots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(Path.Combine(home, "Plans")), roots, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(repoPath), roots, StringComparer.OrdinalIgnoreCase);
        Assert.Single(roots, r => string.Equals(r, Path.GetFullPath(extraRoot), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComputeRoots_ExpandsTildeAndEnvironmentVariables()
    {
        var home = CreateTempDir();
        const string varName = "TENDRIL_LFRP_TEST_ROOT";
        var varTarget = CreateTempDir();
        Environment.SetEnvironmentVariable(varName, varTarget);
        try
        {
            var settings = new TendrilSettings
            {
                Security = new SecuritySettings
                {
                    LocalFileRoots = new List<string> { $"${varName}" }
                }
            };

            var config = new ConfigService(settings, home);
            var roots = LocalFileRootPolicy.ComputeRoots(config);

            Assert.Contains(Path.GetFullPath(varTarget), roots, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void TryResolve_FileDirectlyInRoot_Allowed()
    {
        var root = CreateTempDir();
        var file = Path.Combine(root, "image.png");

        var allowed = LocalFileRootPolicy.TryResolve(file, new List<string> { root }, out var resolved);

        Assert.True(allowed);
        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Fact]
    public void TryResolve_FileNestedUnderRoot_Allowed()
    {
        var root = CreateTempDir();
        var file = Path.Combine(root, "nested", "deep", "image.png");

        var allowed = LocalFileRootPolicy.TryResolve(file, new List<string> { root }, out _);

        Assert.True(allowed);
    }

    [Fact]
    public void TryResolve_SiblingDirectorySharingRootPrefix_Rejected()
    {
        var parent = CreateTempDir();
        var root = Path.Combine(parent, "pub");
        var sibling = Path.Combine(parent, "pub-secrets", "x.png");
        Directory.CreateDirectory(root);

        var allowed = LocalFileRootPolicy.TryResolve(sibling, new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_TraversalOutsideRoot_Rejected()
    {
        var parent = CreateTempDir();
        var root = Path.Combine(parent, "root");
        Directory.CreateDirectory(root);
        var traversal = Path.Combine(root, "..", "outside", "x.png");

        var allowed = LocalFileRootPolicy.TryResolve(traversal, new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_SymlinkInsideRootPointingOutside_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDir();
        var outside = CreateTempDir();
        var outsideFile = Path.Combine(outside, "secret.png");
        File.WriteAllText(outsideFile, "");
        var linkPath = Path.Combine(root, "link.png");
        File.CreateSymbolicLink(linkPath, outsideFile);

        var allowed = LocalFileRootPolicy.TryResolve(linkPath, new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_EmptyRootList_RejectsEverything()
    {
        var allowed = LocalFileRootPolicy.TryResolve("/anything/at/all.png", new List<string>(), out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_SymlinkedDirectoryInsideRootPointingOutside_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDir();
        var outside = CreateTempDir();
        var outsideFile = Path.Combine(outside, "photo.png");
        File.WriteAllText(outsideFile, "");
        var sharedLink = Path.Combine(root, "shared");
        Directory.CreateSymbolicLink(sharedLink, outside);

        var allowed = LocalFileRootPolicy.TryResolve(Path.Combine(sharedLink, "photo.png"), new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_SymlinkedDirectoryMidPathWithNestedFile_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDir();
        var outside = CreateTempDir();
        var sharedLink = Path.Combine(root, "shared");
        Directory.CreateSymbolicLink(sharedLink, outside);

        var allowed = LocalFileRootPolicy.TryResolve(
            Path.Combine(sharedLink, "nested", "photo.png"), new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_SymlinkedDirectoryPointingBackInsideRoot_Allowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDir();
        var real = Path.Combine(root, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "photo.png"), "");
        var alias = Path.Combine(root, "alias");
        Directory.CreateSymbolicLink(alias, real);

        var allowed = LocalFileRootPolicy.TryResolve(Path.Combine(alias, "photo.png"), new List<string> { root }, out _);

        Assert.True(allowed);
    }

    [Fact]
    public void TryResolve_RootItselfBehindSymlink_Allowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = CreateTempDir();
        var real = Path.Combine(parent, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "photo.png"), "");
        var link = Path.Combine(parent, "link");
        Directory.CreateSymbolicLink(link, real);

        var settings = new TendrilSettings
        {
            Security = new SecuritySettings { LocalFileRoots = new List<string> { link } }
        };
        var config = new ConfigService(settings, CreateTempDir());
        var roots = LocalFileRootPolicy.ComputeRoots(config);

        var allowed = LocalFileRootPolicy.TryResolve(Path.Combine(link, "photo.png"), roots, out _);

        Assert.True(allowed);
    }

    [Fact]
    public void TryResolve_SymlinkLoop_Rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = CreateTempDir();
        var a = Path.Combine(root, "a");
        var b = Path.Combine(root, "b");
        Directory.CreateSymbolicLink(a, b);
        Directory.CreateSymbolicLink(b, a);

        var allowed = LocalFileRootPolicy.TryResolve(Path.Combine(a, "x.png"), new List<string> { root }, out _);

        Assert.False(allowed);
    }

    [Fact]
    public void ComputeRoots_IncludesResolvedFormOfEachRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var parent = CreateTempDir();
        var real = Path.Combine(parent, "real");
        Directory.CreateDirectory(real);
        var link = Path.Combine(parent, "link");
        Directory.CreateSymbolicLink(link, real);

        var settings = new TendrilSettings
        {
            Security = new SecuritySettings { LocalFileRoots = new List<string> { link } }
        };
        var config = new ConfigService(settings, CreateTempDir());
        var roots = LocalFileRootPolicy.ComputeRoots(config);

        Assert.Contains(Path.GetFullPath(link), roots, StringComparer.OrdinalIgnoreCase);
        Assert.True(LocalFileRootPolicy.TryResolveRealPath(link, out var resolvedLink));
        Assert.Contains(resolvedLink, roots, StringComparer.OrdinalIgnoreCase);
    }
}
