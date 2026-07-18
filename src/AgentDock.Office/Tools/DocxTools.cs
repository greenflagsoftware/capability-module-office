using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AgentDock.Office.Tools;

/// <summary>
/// MCP tools that shell out to the AgentDock.Office.CLI for docx operations.
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
    /// Escapes a single argument value for the command line.
    /// </summary>
    private static string EscapeArg(string value)
    {
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    /// <summary>
    /// Builds a --root argument if the configured root is not the default.
    /// </summary>
    private static string RootArg()
    {
        var root = ResolveRoot();
        var cwd = Directory.GetCurrentDirectory();
        return string.Equals(root, cwd, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" --root {EscapeArg(root)}";
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
    /// </summary>
    private static async Task<JsonDocument> CallCliAsync(string arguments, TimeSpan? timeout = null)
    {
        string json;
        try
        {
            json = await CliRunner.RunAsync(arguments, timeout);
        }
        catch (CliToolException ex)
        {
            // Re-throw as InvalidOperationException so the MCP framework
            // surfaces the stderr content, not just a generic error.
            throw new InvalidOperationException(
                $"CLI tool call failed. {ex.Message}");
        }
        catch (CliTimeoutException)
        {
            throw;
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"CLI binary not found: {ex.FileName}. The module may not be deployed correctly.");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "CLI tool produced empty output. This may indicate an internal error.");
        }

        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"CLI tool produced malformed JSON output: {ex.Message}. Raw output (first 200 chars): {json[..Math.Min(json.Length, 200)]}");
        }
    }

    [McpServerTool, Description("Read the plain text content of a .docx file.")]
    public static async Task<string> DocxRead(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync($"docx read {EscapeArg(path)}{RootArg()}");
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

        using var doc = await CallCliAsync(
            $"docx create {EscapeArg(path)} --title {EscapeArg(title ?? "Document")} --content {EscapeArg(content ?? "")}{RootArg()}");

        var resolved = doc.RootElement.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        return $"Created .docx at {resolved}";
    }

    [McpServerTool, Description("Get metadata about a .docx document (paragraph count, word count, character count).")]
    public static async Task<string> DocxInfo(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync($"docx info {EscapeArg(path)}{RootArg()}");

        var root = doc.RootElement;

        var paraCount = root.TryGetProperty("paragraphCount", out var pc) ? pc.GetInt32() : 0;
        var wordCount = root.TryGetProperty("wordCount", out var wc) ? wc.GetInt32() : 0;
        var charCount = root.TryGetProperty("charCount", out var cc) ? cc.GetInt32() : 0;

        return $"Paragraphs: {paraCount}\nWords: {wordCount}\nCharacters: {charCount}";
    }
}