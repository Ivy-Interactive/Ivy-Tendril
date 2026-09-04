using System.Net;
using System.Text;
using System.Text.Json;
using Ivy.Tendril.Widgets;

namespace Ivy.Tendril.Test.Widgets;

public class SourceMapReaderTests
{
    private const string Origin = "https://app.test";

    private const string Vlq64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>Base64-VLQ encode one signed value, so fixtures are generated not hand-written.</summary>
    private static string Vlq(int value)
    {
        var v = value < 0 ? ((-value) << 1) | 1 : value << 1;
        var sb = new StringBuilder();
        do
        {
            var digit = v & 31;
            v >>= 5;
            if (v > 0) digit |= 32;
            sb.Append(Vlq64[digit]);
        } while (v > 0);
        return sb.ToString();
    }

    /// <summary>Segments on one generated line, as absolute values; deltas are derived here.</summary>
    private static string Mappings((int GenCol, int Src, int Line, int Col, int Name)[] segments)
    {
        int genCol = 0, src = 0, line = 0, col = 0, name = 0;
        var parts = new List<string>();
        foreach (var s in segments)
        {
            var encoded = Vlq(s.GenCol - genCol) + Vlq(s.Src - src) + Vlq(s.Line - line) + Vlq(s.Col - col);
            if (s.Name >= 0) encoded += Vlq(s.Name - name);
            genCol = s.GenCol; src = s.Src; line = s.Line; col = s.Col;
            if (s.Name >= 0) name = s.Name;
            parts.Add(encoded);
        }
        return string.Join(",", parts);
    }

    // One generated line: two columns onto src/SaveButton.tsx (the second at original line
    // 42) and one onto a dependency, so ignore-list handling has something to filter.
    private static string BuildMap(bool withIgnoreList = true, bool withContent = true)
    {
        var map = new Dictionary<string, object?>
        {
            ["version"] = 3,
            ["file"] = "chunk.js",
            ["sources"] = new[] { "src/SaveButton.tsx", "node_modules/react/index.js" },
            ["names"] = new[] { "SaveButton" },
            ["mappings"] = Mappings(
            [
                (GenCol: 0,  Src: 0, Line: 0,  Col: 0, Name: 0),
                (GenCol: 10, Src: 0, Line: 41, Col: 6, Name: -1),   // -> SaveButton.tsx:42
                (GenCol: 20, Src: 1, Line: 0,  Col: 0, Name: -1),   // -> node_modules
            ]),
        };
        if (withContent)
        {
            map["sourcesContent"] = new[]
            {
                string.Join("\n", Enumerable.Range(1, 60).Select(i => $"line {i} of SaveButton")),
                "module.exports = {};",
            };
        }
        if (withIgnoreList) map["x_google_ignoreList"] = new[] { 1 };
        return JsonSerializer.Serialize(map);
    }

    private static SourceMapReader ReaderFor(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new StubHandler(handler)));

    private static HttpResponseMessage Ok(string body, string contentType = "application/javascript") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    [Fact]
    public async Task Resolves_A_Frame_Through_An_External_Map()
    {
        var reader = ReaderFor(request => request.RequestUri!.AbsolutePath.EndsWith(".map")
            ? Ok(BuildMap(), "application/json")
            : Ok("var a=1;\n//# sourceMappingURL=chunk.js.map"));

        var resolved = await reader.ResolveAsync(
            [new StackFrame($"{Origin}/assets/chunk.js", 1, 10)], null, default);

        var frame = Assert.Single(resolved);
        Assert.Equal("src/SaveButton.tsx", frame.File);
        Assert.Equal(42, frame.Line);
        Assert.False(frame.IsThirdParty);
    }

    [Fact]
    public async Task Resolves_A_Frame_Through_An_Inline_Base64_Map()
    {
        var inline = Convert.ToBase64String(Encoding.UTF8.GetBytes(BuildMap()));
        var reader = ReaderFor(_ => Ok(
            "var a=1;\n//# sourceMappingURL=data:application/json;base64," + inline));

        var resolved = await reader.ResolveAsync(
            [new StackFrame($"{Origin}/assets/chunk.js", 1, 10)], null, default);

        Assert.Equal("src/SaveButton.tsx", Assert.Single(resolved).File);
    }

    [Fact]
    public async Task Resolves_A_Relative_SourceMappingUrl_Against_The_Script()
    {
        Uri? requestedMap = null;
        var reader = ReaderFor(request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith(".map"))
                return Ok("var a=1;\n//# sourceMappingURL=../maps/chunk.js.map");
            requestedMap = request.RequestUri;
            return Ok(BuildMap(), "application/json");
        });

        await reader.ResolveAsync([new StackFrame($"{Origin}/assets/js/chunk.js", 1, 10)], null, default);

        Assert.Equal($"{Origin}/assets/maps/chunk.js.map", requestedMap?.AbsoluteUri);
    }

    // x_google_ignoreList is the standard "not the developer's code" marker; without it the
    // top frames of any stack are framework internals and the answer is always wrong.
    [Fact]
    public async Task Marks_Ignore_Listed_Sources_As_Third_Party()
    {
        var reader = ReaderFor(request => request.RequestUri!.AbsolutePath.EndsWith(".map")
            ? Ok(BuildMap(), "application/json")
            : Ok("var a=1;\n//# sourceMappingURL=chunk.js.map"));

        var resolved = await reader.ResolveAsync(
            [new StackFrame($"{Origin}/assets/chunk.js", 1, 20)], null, default);

        Assert.True(Assert.Single(resolved).IsThirdParty);
    }

    [Fact]
    public async Task Returns_A_Code_Frame_From_SourcesContent()
    {
        var reader = ReaderFor(request => request.RequestUri!.AbsolutePath.EndsWith(".map")
            ? Ok(BuildMap(), "application/json")
            : Ok("var a=1;\n//# sourceMappingURL=chunk.js.map"));

        var resolved = await reader.ResolveAsync(
            [new StackFrame($"{Origin}/assets/chunk.js", 1, 10)], null, default);

        var codeFrame = Assert.Single(resolved).CodeFrame;
        Assert.NotNull(codeFrame);
        Assert.Contains("> 42 | line 42 of SaveButton", codeFrame);
        Assert.Contains("  41 | line 41 of SaveButton", codeFrame);
    }

    [Fact]
    public async Task Skips_A_Script_With_No_Map()
    {
        var reader = ReaderFor(_ => Ok("var a=1;"));
        Assert.Empty(await reader.ResolveAsync([new StackFrame($"{Origin}/a.js", 1, 0)], null, default));
    }

    [Fact]
    public async Task Skips_A_Malformed_Map_Rather_Than_Guessing()
    {
        var reader = ReaderFor(request => request.RequestUri!.AbsolutePath.EndsWith(".map")
            ? Ok("{ not json", "application/json")
            : Ok("var a=1;\n//# sourceMappingURL=chunk.js.map"));

        Assert.Empty(await reader.ResolveAsync([new StackFrame($"{Origin}/a.js", 1, 0)], null, default));
    }

    // The endpoint fetches caller-named URLs, so the allow-list is the same SSRF gate the
    // proxy itself uses. A blocked URL must never reach the network.
    [Fact]
    public async Task Honours_The_Url_Allow_List()
    {
        var reached = false;
        var reader = ReaderFor(_ => { reached = true; return Ok("var a=1;"); });

        var resolved = await reader.ResolveAsync(
            [new StackFrame($"{Origin}/a.js", 1, 0)], _ => false, default);

        Assert.Empty(resolved);
        Assert.False(reached);
    }

    [Theory]
    [InlineData("webpack://my-app/./src/App.tsx", "src/App.tsx")]
    [InlineData("webpack:///src/App.tsx", "src/App.tsx")]
    [InlineData("vite:src/main.ts", "src/main.ts")]
    [InlineData("../../src/App.tsx", "src/App.tsx")]
    [InlineData("src/App.tsx", "src/App.tsx")]
    public void NormalizeSourcePath_Yields_A_Repo_Relative_Path(string raw, string expected)
    {
        Assert.Equal(expected, SourceMapReader.NormalizeSourcePath(raw));
    }

    [Theory]
    [InlineData("node_modules/react/index.js", true)]
    [InlineData("webpack/bootstrap", true)]
    [InlineData("src/App.tsx", false)]
    public void LooksThirdParty_Flags_Dependency_Paths(string path, bool expected)
    {
        Assert.Equal(expected, SourceMapReader.LooksThirdParty(path));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(handler(request));
    }
}
