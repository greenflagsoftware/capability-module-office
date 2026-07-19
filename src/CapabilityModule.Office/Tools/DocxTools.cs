using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CapabilityModule.Office.Tools;

/// <summary>
/// MCP tools that shell out to the CapabilityModule.Office.CLI for docx operations.
/// Each method maps to a CLI subcommand invocation. Input validation,
/// timeout handling, and malformed-output recovery are applied per the
/// Phase 4 hardening requirements.
/// </summary>
[McpServerToolType]
public static class DocxTools
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

    /// <summary>
    /// Builds the full CLI argument list for a docx subcommand, appending
    /// --root only if the configured root differs from the CLI's default.
    /// Each element is passed through as its own argv entry (no manual
    /// quoting/escaping) so content containing backslashes or quotes round-trips
    /// intact — see <see cref="CliRunner.RunAsync(IReadOnlyList{string}, TimeSpan?)"/>.
    /// </summary>
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
    /// Validates that a path is non-null, non-empty, and not just whitespace.
    /// </summary>
    private static void ValidatePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null or empty.", paramName);
        }
    }

    /// <summary>
    /// Calls the CLI and parses the JSON result, catching and wrapping
    /// known failure modes (malformed JSON, non-zero exit, timeout).
    ///
    /// Failures are surfaced as <see cref="McpException"/> specifically because
    /// the MCP SDK only forwards an exception's <c>Message</c> to the calling
    /// client for that type — any other exception type is replaced with a
    /// generic "An error occurred invoking '{tool}'." before it leaves the
    /// server, hiding the real cause (verified against ModelContextProtocol.Core
    /// 1.4.0 behavior).
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

    [McpServerTool, Description("Read the plain text content of a .docx file.")]
    public static async Task<string> DocxRead(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync(BuildArgs("docx", "read", path));
        var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() : "";
        return content ?? "";
    }

    [McpServerTool, Description("Create a new .docx document from text content.")]
    public static async Task<string> DocxCreate(
        [Description("Path for the new .docx file, relative to the restricted root.")] string path,
        [Description("Document title (used as the first heading).")] string title,
        [Description("Text content for the document body.")] string content)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync(BuildArgs(
            "docx", "create", path, "--title", title ?? "Document", "--content", content ?? ""));

        var resolved = doc.RootElement.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        return $"Created .docx at {resolved}";
    }

    [McpServerTool, Description("Get metadata about a .docx document (paragraph count, word count, character count).")]
    public static async Task<string> DocxInfo(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync(BuildArgs("docx", "info", path));

        var root = doc.RootElement;

        var paraCount = root.TryGetProperty("paragraphCount", out var pc) ? pc.GetInt32() : 0;
        var wordCount = root.TryGetProperty("wordCount", out var wc) ? wc.GetInt32() : 0;
        var charCount = root.TryGetProperty("charCount", out var cc) ? cc.GetInt32() : 0;

        return $"Paragraphs: {paraCount}\nWords: {wordCount}\nCharacters: {charCount}";
    }
}