using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CapabilityModule.Office.Tools;

/// <summary>
/// MCP tools that shell out to the CapabilityModule.Office.CLI for index
/// operations — building the index and searching it.
/// </summary>
[McpServerToolType]
public static class IndexTools
{
    private static string ResolveRoot()
    {
        var env = Environment.GetEnvironmentVariable("OFFICE_CLI_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    private static List<string> BuildArgs(params string[] baseArgs)
    {
        var args = new List<string>(baseArgs);

        var root = ResolveRoot();
        var cwd = Directory.GetCurrentDirectory();
        if (!string.Equals(root, cwd, StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--root");
            args.Add(root);
        }

        return args;
    }

    /// <summary>
    /// Calls the CLI and parses the JSON result, catching and wrapping
    /// known failure modes (malformed JSON, non-zero exit, timeout).
    ///
    /// Failures are surfaced as <see cref="McpException"/> specifically because
    /// the MCP SDK only forwards an exception's <c>Message</c> to the calling
    /// client for that type — any other exception type is replaced with a
    /// generic "An error occurred invoking '{tool}'." before it leaves the
    /// server, hiding the real cause.
    /// </summary>
    private static async Task<JsonDocument> CallCliAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        string json;
        try
        {
            json = await CliRunner.RunAsync(arguments, timeout);
        }
        catch (CliToolException ex)
        {
            throw new McpException($"CLI tool call failed. {ex.Message}");
        }
        catch (CliTimeoutException ex)
        {
            throw new McpException(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            throw new McpException(
                $"CLI binary not found: {ex.FileName}. The module may not be deployed correctly.");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new McpException(
                "CLI tool produced empty output. This may indicate an internal error.");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new McpException(
                $"CLI tool produced malformed JSON output: {ex.Message}. Raw output (first 200 chars): {json[..Math.Min(json.Length, 200)]}");
        }
    }

    [McpServerTool, Description("Search indexed document content using hybrid (vector + keyword) search. Returns relevant chunks from indexed documents, ranked by relevance. Requires the index to have been built first via index_build.")]
    public static async Task<string> IndexSearch(
        [Description("Free-text search query. The query is embedded for vector similarity and also used for keyword matching (e.g. 'quarterly financial report 2024').")] string query,
        [Description("Subdirectory to scope the search under, relative to the restricted root. Defaults to the root directory.")] string? path = null,
        [Description("Maximum number of results to return (1–100). Defaults to 10.")] int? limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must not be null or empty.", nameof(query));
        }

        var args = BuildArgs("index", "search", query);
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add(path);
        }
        args.Add("--limit");
        args.Add(Math.Clamp(limit ?? 10, 1, 100).ToString());

        using var doc = await CallCliAsync(args, CliRunner.DefaultTimeout);

        var root = doc.RootElement;
        var entries = root.TryGetProperty("entries", out var e) ? e : default;

        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
        {
            return "No results found.";
        }

        var results = new List<string>();
        foreach (var entry in entries.EnumerateArray())
        {
            var documentPath = entry.TryGetProperty("documentPath", out var dp) ? dp.GetString() : "";
            var text = entry.TryGetProperty("text", out var t) ? t.GetString() : "";
            var score = entry.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0;
            var headingPath = entry.TryGetProperty("headingPath", out var hp) ? hp : default;

            var headingInfo = "";
            if (headingPath.ValueKind == JsonValueKind.Array && headingPath.GetArrayLength() > 0)
            {
                var parts = new List<string>();
                foreach (var h in headingPath.EnumerateArray())
                {
                    parts.Add(h.GetString() ?? "");
                }
                headingInfo = $" [{string.Join(" > ", parts)}]";
            }

            // Truncate text to first ~200 chars for the summary
            var textPreview = text?.Length > 200 ? text[..200] + "..." : text;

            results.Add($"  ({score:F4}) {documentPath}{headingInfo}\n    {textPreview}");
        }

        var totalResults = root.TryGetProperty("totalResults", out var tr) ? tr.GetInt32() : 0;
        return $"Found {totalResults} result(s):\n" + string.Join("\n\n", results);
    }

    [McpServerTool, Description("Build a search index over documents in the restricted root. Walks files, extracts text, chunks content, and optionally generates vector embeddings for semantic search. Idempotent — re-running only processes changed files.")]
    public static async Task<string> IndexBuild(
        [Description("Directory to index (relative to the restricted root). Defaults to the root directory.")] string? path = null,
        [Description("Whether to generate vector embeddings for chunks. Defaults to true. Set to false to skip embedding for cost control.")] bool? embed = true)
    {
        var args = BuildArgs("index", "build");
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add(path);
        }

        if (embed == false)
        {
            args.Add("--embed");
            args.Add("false");
        }

        using var doc = await CallCliAsync(args, TimeSpan.FromMinutes(5)); // Index build may be long-running

        var root = doc.RootElement;

        var filesProcessed = root.TryGetProperty("filesProcessed", out var fp) ? fp.GetInt32() : 0;
        var filesIndexed = root.TryGetProperty("filesIndexed", out var fi) ? fi.GetInt32() : 0;
        var filesUnchanged = root.TryGetProperty("filesUnchanged", out var fu) ? fu.GetInt32() : 0;
        var filesSkipped = root.TryGetProperty("filesSkipped", out var fs) ? fs.GetInt32() : 0;
        var filesWithErrors = root.TryGetProperty("filesWithErrors", out var fwe) ? fwe.GetInt32() : 0;
        var totalChunksWritten = root.TryGetProperty("totalChunksWritten", out var tcw) ? tcw.GetInt32() : 0;
        var totalChunksEmbedded = root.TryGetProperty("totalChunksEmbedded", out var tce) ? tce.GetInt32() : 0;
        var existingChunksEmbedded = root.TryGetProperty("existingChunksEmbedded", out var ece) ? ece.GetInt32() : 0;

        var lines = new List<string>
        {
            $"Files processed: {filesProcessed}",
            $"  Indexed: {filesIndexed}",
            $"  Unchanged: {filesUnchanged}",
            $"  Skipped (unsupported format): {filesSkipped}",
            $"  Errors: {filesWithErrors}",
            $"Chunks written: {totalChunksWritten}",
            $"Chunks embedded (new): {totalChunksEmbedded}",
            $"Chunks embedded (existing backfill): {existingChunksEmbedded}",
        };

        return string.Join("\n", lines);
    }
}