using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ivy.Tendril.Widgets;

/// <summary>A JS stack frame as the browser reported it.</summary>
public sealed record StackFrame(string Url, int Line, int Col);

/// <summary>A frame resolved back to the original source through a source map.</summary>
public sealed record ResolvedFrame
{
    /// <summary>Original source path, normalised (e.g. <c>src/components/SaveButton.tsx</c>).</summary>
    public string? File { get; init; }

    /// <summary>1-based line in the original source.</summary>
    public int Line { get; init; }

    /// <summary>0-based column in the original source.</summary>
    public int Col { get; init; }

    /// <summary>Symbol name recorded in the map, when it has one.</summary>
    public string? Name { get; init; }

    /// <summary>Framework or dependency code rather than the app's own.</summary>
    public bool IsThirdParty { get; init; }

    /// <summary>±<c>context</c> lines of the original source, target line marked with <c>&gt;</c>.</summary>
    public string? CodeFrame { get; init; }

    /// <summary>The frame this came from, so a caller can correlate.</summary>
    public StackFrame? Frame { get; init; }
}

/// <summary>
/// Resolves minified JS positions back to original sources using the bundle's source map.
///
/// Source maps are the one thing every modern toolchain agrees on: they carry the original
/// paths, the original text (<c>sourcesContent</c>) and — crucially for us —
/// <c>x_google_ignoreList</c>, the standard marker for "this frame is vendor code, not the
/// app's". That makes a single server-side resolver a viable substitute for per-framework
/// source lookup, which is why the collector in agent.js normalises everything to raw frames.
///
/// Deliberately dependency-free and small: VLQ decoding is well understood, and the widget
/// already ships its own assets rather than pulling in packages.
/// </summary>
public sealed class SourceMapReader(HttpClient httpClient, long cacheByteBudget = 64L * 1024 * 1024)
{
    private static readonly Regex SourceMappingUrl = new(
        @"//[#@]\s*sourceMappingURL=(?<url>[^\s'""]+)\s*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Paths a bundler uses for code that is not the app's own.
    private static readonly string[] VendorMarkers =
        ["node_modules", "webpack/bootstrap", "webpack-internal:", "/~/", "\\node_modules\\"];

    private readonly ConcurrentDictionary<string, Task<SourceMap?>> _cache = new();
    private long _cachedBytes;

    /// <summary>Largest map we will download and parse. Bundles can emit enormous maps.</summary>
    public int MaxMapBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>Lines of original source either side of the target in <see cref="ResolvedFrame.CodeFrame"/>.</summary>
    public int CodeFrameContext { get; init; } = 10;

    /// <summary>
    /// Resolve frames in order. Frames whose map cannot be fetched or parsed are skipped
    /// rather than guessed at — a wrong file is worse than none.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedFrame>> ResolveAsync(
        IEnumerable<StackFrame> frames, Func<Uri, bool>? isUrlAllowed, CancellationToken ct)
    {
        var resolved = new List<ResolvedFrame>();
        foreach (var frame in frames)
        {
            if (!Uri.TryCreate(frame.Url, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            if (isUrlAllowed is not null && !isUrlAllowed(uri)) continue;

            var map = await GetMapAsync(uri, isUrlAllowed, ct);
            if (map is null) continue;

            if (map.Lookup(frame.Line, frame.Col) is not { } hit) continue;
            resolved.Add(hit with
            {
                File = QualifyWithRequestPath(hit.File, uri),
                Frame = frame,
                CodeFrame = map.BuildCodeFrame(hit, CodeFrameContext),
            });
        }
        return resolved;
    }

    private Task<SourceMap?> GetMapAsync(Uri scriptUri, Func<Uri, bool>? isUrlAllowed, CancellationToken ct) =>
        _cache.GetOrAdd(scriptUri.AbsoluteUri, _ => LoadMapAsync(scriptUri, isUrlAllowed, ct));

    private async Task<SourceMap?> LoadMapAsync(Uri scriptUri, Func<Uri, bool>? isUrlAllowed, CancellationToken ct)
    {
        try
        {
            var script = await httpClient.GetStringAsync(scriptUri, ct);
            var match = SourceMappingUrl.Matches(script).LastOrDefault();
            if (match is null) return null;

            var reference = match.Groups["url"].Value.Trim();
            string json;

            if (reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = reference.IndexOf(',');
                if (comma < 0) return null;
                var payload = reference[(comma + 1)..];
                json = reference[..comma].Contains("base64", StringComparison.OrdinalIgnoreCase)
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(payload))
                    : Uri.UnescapeDataString(payload);
            }
            else
            {
                if (!Uri.TryCreate(scriptUri, reference, out var mapUri)) return null;
                if (isUrlAllowed is not null && !isUrlAllowed(mapUri)) return null;

                using var response = await httpClient.GetAsync(mapUri, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > MaxMapBytes) return null;
                json = await response.Content.ReadAsStringAsync(ct);
            }

            if (json.Length > MaxMapBytes) return null;

            var map = SourceMap.Parse(json);
            if (map is not null) TrackCacheSize(json.Length);
            return map;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or FormatException
                                    or OperationCanceledException or TaskCanceledException)
        {
            return null;
        }
    }

    // Crude but sufficient: once the budget is spent, stop retaining new maps rather than
    // evicting live ones, so a long session cannot grow without bound.
    private void TrackCacheSize(long bytes)
    {
        if (Interlocked.Add(ref _cachedBytes, bytes) <= cacheByteBudget) return;
        _cache.Clear();
        Interlocked.Exchange(ref _cachedBytes, 0);
    }

    /// <summary>
    /// Some maps — Vite's dev transform among them — record only the bare file name, which
    /// leaves an agent to go searching for it. The URL the script was served from usually
    /// carries the directory, so borrow it when the file names agree.
    /// </summary>
    private static string? QualifyWithRequestPath(string? file, Uri scriptUri)
    {
        if (string.IsNullOrEmpty(file) || file.Contains('/')) return file;

        var path = scriptUri.AbsolutePath.TrimStart('/');
        return path.EndsWith('/' + file, StringComparison.OrdinalIgnoreCase) ? path : file;
    }

    /// <summary>Does this original path look like dependency or framework code?</summary>
    public static bool LooksThirdParty(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return VendorMarkers.Any(marker => path.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Strip the scheme-ish prefixes bundlers put in front of original paths so the result
    /// reads like a repo path: <c>webpack://app/./src/App.tsx</c> becomes <c>src/App.tsx</c>.
    /// </summary>
    public static string NormalizeSourcePath(string path)
    {
        var value = path;

        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            value = value[(schemeEnd + 3)..];
            var slash = value.IndexOf('/');
            if (slash >= 0 && !value.StartsWith("./", StringComparison.Ordinal)) value = value[(slash + 1)..];
        }
        else if (value.StartsWith("vite:", StringComparison.OrdinalIgnoreCase))
        {
            value = value["vite:".Length..];
        }

        value = value.Replace('\\', '/');
        while (value.StartsWith("./", StringComparison.Ordinal)) value = value[2..];
        while (value.StartsWith("../", StringComparison.Ordinal)) value = value[3..];
        return value.TrimStart('/');
    }

    // ---- the map itself -------------------------------------------------------

    private sealed class SourceMap
    {
        private string[] _sources = [];
        private string?[] _sourcesContent = [];
        private string[] _names = [];
        private HashSet<int> _ignored = [];

        // Segments per generated line, ordered by generated column so a lookup is a
        // binary search rather than a scan of a file that can hold millions of them.
        private List<Segment>[] _lines = [];

        private readonly record struct Segment(int GenCol, int SourceIndex, int SourceLine, int SourceCol, int NameIndex);

        public static SourceMap? Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // Index maps ("sections") are rare enough to decline rather than half-support.
            if (!root.TryGetProperty("mappings", out var mappingsElement)) return null;

            var map = new SourceMap
            {
                _sources = ReadStrings(root, "sources"),
                _names = ReadStrings(root, "names"),
            };

            var sourceRoot = root.TryGetProperty("sourceRoot", out var sr) ? sr.GetString() ?? "" : "";
            if (sourceRoot.Length > 0)
            {
                map._sources = map._sources
                    .Select(s => s.StartsWith('/') || s.Contains("://") ? s : $"{sourceRoot.TrimEnd('/')}/{s}")
                    .ToArray();
            }

            if (root.TryGetProperty("sourcesContent", out var contents) && contents.ValueKind == JsonValueKind.Array)
            {
                map._sourcesContent = contents.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                    .ToArray();
            }

            // The standard "not the developer's code" signal, emitted by Vite and webpack.
            if (root.TryGetProperty("x_google_ignoreList", out var ignore) && ignore.ValueKind == JsonValueKind.Array)
            {
                map._ignored = ignore.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number)
                    .Select(e => e.GetInt32())
                    .ToHashSet();
            }

            map.DecodeMappings(mappingsElement.GetString() ?? "");
            return map;
        }

        private static string[] ReadStrings(JsonElement root, string property) =>
            root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.Array
                ? element.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : [];

        private void DecodeMappings(string mappings)
        {
            var lines = new List<List<Segment>>();
            var current = new List<Segment>();
            int sourceIndex = 0, sourceLine = 0, sourceCol = 0, nameIndex = 0, genCol = 0;
            var position = 0;

            while (position < mappings.Length)
            {
                var c = mappings[position];
                if (c == ';')
                {
                    lines.Add(current);
                    current = [];
                    genCol = 0;           // generated column resets each line; the rest carry over
                    position++;
                    continue;
                }
                if (c == ',') { position++; continue; }

                genCol += DecodeVlq(mappings, ref position);
                var fields = 1;
                var segSource = sourceIndex;
                var segLine = sourceLine;
                var segCol = sourceCol;
                var segName = -1;

                if (position < mappings.Length && mappings[position] is not (';' or ','))
                {
                    sourceIndex += DecodeVlq(mappings, ref position);
                    sourceLine += DecodeVlq(mappings, ref position);
                    sourceCol += DecodeVlq(mappings, ref position);
                    segSource = sourceIndex;
                    segLine = sourceLine;
                    segCol = sourceCol;
                    fields = 4;

                    if (position < mappings.Length && mappings[position] is not (';' or ','))
                    {
                        nameIndex += DecodeVlq(mappings, ref position);
                        segName = nameIndex;
                        fields = 5;
                    }
                }

                // A 1-field segment marks generated code with no original counterpart.
                if (fields >= 4) current.Add(new Segment(genCol, segSource, segLine, segCol, segName));
            }
            lines.Add(current);

            _lines = lines.Select(l => { l.Sort((a, b) => a.GenCol.CompareTo(b.GenCol)); return l; }).ToArray();
        }

        private static int DecodeVlq(string value, ref int position)
        {
            const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            int result = 0, shift = 0;
            while (position < value.Length)
            {
                var digit = Alphabet.IndexOf(value[position++]);
                if (digit < 0) break;
                var hasContinuation = (digit & 32) != 0;
                result += (digit & 31) << shift;
                shift += 5;
                if (!hasContinuation) break;
            }
            // Bit 0 is the sign.
            var negative = (result & 1) == 1;
            result >>= 1;
            return negative ? -result : result;
        }

        /// <summary>Look up a 1-based generated line and 0-based generated column.</summary>
        public ResolvedFrame? Lookup(int generatedLine, int generatedColumn)
        {
            var lineIndex = generatedLine - 1;
            if (lineIndex < 0 || lineIndex >= _lines.Length) return null;

            var segments = _lines[lineIndex];
            if (segments.Count == 0) return null;

            // Largest segment starting at or before the column.
            int low = 0, high = segments.Count - 1, found = -1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                if (segments[mid].GenCol <= generatedColumn) { found = mid; low = mid + 1; }
                else high = mid - 1;
            }
            if (found < 0) found = 0;

            var segment = segments[found];
            if (segment.SourceIndex < 0 || segment.SourceIndex >= _sources.Length) return null;

            var raw = _sources[segment.SourceIndex];
            return new ResolvedFrame
            {
                File = NormalizeSourcePath(raw),
                Line = segment.SourceLine + 1,
                Col = segment.SourceCol,
                Name = segment.NameIndex >= 0 && segment.NameIndex < _names.Length ? _names[segment.NameIndex] : null,
                IsThirdParty = _ignored.Contains(segment.SourceIndex) || LooksThirdParty(raw),
            };
        }

        /// <summary>
        /// A window of the ORIGINAL source around the hit, straight out of sourcesContent —
        /// the highest-value part of the payload, since the fixing agent gets the real code
        /// without another round trip.
        /// </summary>
        public string? BuildCodeFrame(ResolvedFrame hit, int context)
        {
            var index = Array.FindIndex(_sources, s => NormalizeSourcePath(s) == hit.File);
            if (index < 0 || index >= _sourcesContent.Length) return null;
            if (_sourcesContent[index] is not { } content) return null;

            var lines = content.Replace("\r\n", "\n").Split('\n');
            var first = Math.Max(0, hit.Line - 1 - context);
            var last = Math.Min(lines.Length - 1, hit.Line - 1 + context);
            if (first > last) return null;

            var width = (last + 1).ToString().Length;
            var builder = new StringBuilder();
            for (var i = first; i <= last; i++)
            {
                var marker = i == hit.Line - 1 ? '>' : ' ';
                builder.Append(marker).Append(' ')
                    .Append((i + 1).ToString().PadLeft(width)).Append(" | ")
                    .Append(lines[i]).Append('\n');
            }
            return builder.ToString();
        }
    }
}
