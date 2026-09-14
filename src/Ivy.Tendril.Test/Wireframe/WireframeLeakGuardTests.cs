using System.Diagnostics;
using Ivy.Tendril.Services.Wireframes;

namespace Ivy.Tendril.Test.Wireframe;

/// <summary>
/// The leak guard against a real git repo and a real worktree under a plan folder, the layout
/// ExecutePlan works in.
/// </summary>
public class WireframeLeakGuardTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wireframe-leak-tests", Guid.NewGuid().ToString("N")[..8]);

    private readonly string _repo;
    private readonly string _plan;
    private readonly string _worktree;

    private const string WireframeApp =
        """
        // @tendril-wireframe plan-only: a throwaway mockup. Never copy it into product code.
        import { Badge, Button, Card } from "tendril-wireframes";

        export default function App() {
          const items = ["Visa ending 4242", "Mastercard ending 4444"];
          return (
            <div className="p-6 grid grid-cols-3 gap-6">
              <Card title="Payment">
                <Badge title="Step 2 of 3" />
                {items.map((item) => (
                  <Button key={item} title={item} variant="Outline" />
                ))}
                <Button title="Add a new card" variant="Ghost" />
              </Card>
              <Card title="Order summary">
                <span className="text-sm">3 items in your basket</span>
                <Button title="Pay now" variant="Primary" />
              </Card>
            </div>
          );
        }
        """;

    public WireframeLeakGuardTests()
    {
        _repo = Path.Combine(_root, "repo");
        _plan = Path.Combine(_root, "plans", "00042-CheckoutRedesign");
        _worktree = Path.Combine(_plan, "Worktrees", "repo");

        Directory.CreateDirectory(_repo);
        Git(_repo, "init -b main");
        File.WriteAllText(Path.Combine(_repo, "Checkout.tsx"), "export const Checkout = () => null;\n");
        Git(_repo, "add .");
        Git(_repo, "commit -m base");

        Directory.CreateDirectory(Path.GetDirectoryName(_worktree)!);
        Git(_repo, $"worktree add -b plan/42 \"{_worktree}\"");

        var wireframeSrc = Path.Combine(_plan, "Wireframes", "checkout", "src");
        Directory.CreateDirectory(wireframeSrc);
        File.WriteAllText(Path.Combine(wireframeSrc, "App.tsx"), WireframeApp);
    }

    public void Dispose()
    {
        try { Git(_repo, $"worktree remove --force \"{_worktree}\""); } catch { /* best effort */ }
        WireframeTempRoot.Remove(_root);
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<WireframeLeak> Scan() => WireframeLeakGuard.Scan(_plan, _ => "main");

    [Fact]
    public void A_plan_that_builds_the_screen_with_its_own_components_is_clean()
    {
        File.WriteAllText(Path.Combine(_worktree, "Checkout.tsx"),
            """
            import { SavedCards } from "./SavedCards";

            export const Checkout = ({ cards }: { cards: string[] }) => (
              <section className="checkout">
                <SavedCards cards={cards} />
              </section>
            );
            """);
        Commit("build the checkout");

        Assert.Empty(Scan());
    }

    [Fact]
    public void A_committed_copy_of_a_wireframe_file_is_found()
    {
        File.WriteAllText(Path.Combine(_worktree, "PaymentStep.tsx"), WireframeApp.Replace("    ", "  "));
        Commit("copy the wireframe");

        var leaks = Scan();
        Assert.Contains(leaks, l => l.Kind == WireframeLeakKind.CopiedFile && l.Path == "PaymentStep.tsx");
        Assert.Contains(leaks, l => l.Kind == WireframeLeakKind.Marker);
    }

    [Fact]
    public void Lines_pasted_into_an_existing_file_are_found_even_uncommitted()
    {
        // The component body without the marker, the import or the function header.
        var body = string.Join("\n", WireframeApp.Split('\n').Skip(5).Take(16));
        File.AppendAllText(Path.Combine(_worktree, "Checkout.tsx"), "\nexport const Pasted = () => {\n" + body + "\n};\n");

        var leak = Assert.Single(Scan());
        Assert.Equal(WireframeLeakKind.PastedLines, leak.Kind);
        Assert.Equal("Checkout.tsx", leak.Path);
        Assert.NotNull(leak.Line);
    }

    [Fact]
    public void An_untracked_file_importing_the_library_is_found()
    {
        File.WriteAllText(Path.Combine(_worktree, "Mock.tsx"), "import { Card } from \"tendril-wireframes\";\nexport const Mock = Card;\n");

        var leak = Assert.Single(Scan());
        Assert.Equal(WireframeLeakKind.LibraryReference, leak.Kind);
        Assert.Equal(1, leak.Line);
    }

    [Fact]
    public void A_dependency_on_the_library_is_found()
    {
        File.WriteAllText(Path.Combine(_worktree, "package.json"), "{\n  \"dependencies\": {\n    \"tendril-wireframes\": \"0.1.11\"\n  }\n}\n");
        Commit("add dependency");

        var leak = Assert.Single(Scan());
        Assert.Equal(WireframeLeakKind.LibraryReference, leak.Kind);
    }

    [Fact]
    public void A_wireframe_project_moved_into_the_repo_is_found_by_its_path()
    {
        var src = Path.Combine(_worktree, "docs", "Wireframes", "checkout", "src");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "notes.md"), "Just notes.\n");
        Commit("move the wireframe");

        var leak = Assert.Single(Scan());
        Assert.Equal(WireframeLeakKind.WireframePath, leak.Kind);
    }

    [Fact]
    public void Code_that_merely_lives_in_a_folder_called_Wireframes_is_not_flagged()
    {
        var services = Path.Combine(_worktree, "Services", "Wireframes");
        Directory.CreateDirectory(services);
        File.WriteAllText(Path.Combine(services, "PlanWireframes.cs"), "public static class PlanWireframes { }\n");
        Commit("add a service");

        Assert.Empty(Scan());
    }

    [Fact]
    public void The_report_is_written_for_leaks_and_removed_once_clean()
    {
        File.WriteAllText(Path.Combine(_worktree, "Mock.tsx"), "import { Card } from \"tendril-wireframes\";\n");
        var leaks = Scan();

        WireframeLeakGuard.WriteReport(_plan, leaks);
        var report = File.ReadAllText(WireframeLeakGuard.ReportPath(_plan));
        Assert.Contains("Mock.tsx", report);
        Assert.Contains("## Issues Found", report);
        Assert.DoesNotContain("\u2014", report);

        WireframeLeakGuard.WriteReport(_plan, []);
        Assert.False(File.Exists(WireframeLeakGuard.ReportPath(_plan)));
    }

    [Fact]
    public void A_plan_without_worktrees_has_nothing_to_leak()
    {
        var empty = Path.Combine(_root, "plans", "00043-Empty");
        Directory.CreateDirectory(empty);
        Assert.Empty(WireframeLeakGuard.Scan(empty));
    }

    private void Commit(string message)
    {
        Git(_worktree, "add -A");
        Git(_worktree, $"commit -m \"{message}\"");
    }

    private static void Git(string cwd, string args)
    {
        var psi = new ProcessStartInfo("git", $"-c user.name=test -c user.email=test@example.com -c commit.gpgsign=false {args}")
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {args} failed: {stderr.Result}");
    }
}
