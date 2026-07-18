using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AgentDock.Office.Tools;

/// <summary>
/// MCP tools that shell out to the AgentDock.Office.CLI for docx operations.
/// Each method maps to a CLI subcommand invocation.
/// </summary>
[McpServerToolType]
public static class DocxTools
{
    private static string ResolveRoot()
    {
        // The MCP server's restricted root is configurable. Defaults to a
        // "data" directory next to the server binary, but can be overridden
        // via the OFFICE_CLI_ROOT environment variable (which the CLI reads
        // directly, so we honour it here too for consistency).
        var env = Environment.GetEnvironmentVariable("OFFICE_CLI_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return env;

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    /// <summary>
    /// Escapes a single argument value for the command line so it is safe to
    /// pass through the shell.
    /// </summary>
    private static string EscapeArg(string value)
    {
        // Wrap in double quotes and escape any embedded double quotes and backslashes.
        // This is a simplified approach that works for the arguments we expect.
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    /// <summary>
    /// Builds a —root argument if the configured root is not the default
    /// (current directory).
    /// </summary>
    private static string RootArg()
    {
        var root = ResolveRoot();
        var cwd = Directory.GetCurrentDirectory();
        return string.Equals(root, cwd, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $" --root {EscapeArg(root)}";
    }

    [McpServerTool, Description("Read the plain text content of a .docx file.")]
    public static async Task<string> DocxRead(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        var json = await CliRunner.RunAsync($"docx read {EscapeArg(path)}{RootArg()}");

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.TryGetProperty("content", out var c) ? c.GetString() : "";
        return content ?? "";
    }

    [McpServerTool, Description("Create a new .docx document from text content.")]
    public static async Task<string> DocxCreate(
        [Description("Path for the new .docx file, relative to the restricted root.")] string path,
        [Description("Document title (used as the first heading).")] string title,
        [Description("Text content for the document body.")] string content)
    {
        var json = await CliRunner.RunAsync(
            $"docx create {EscapeArg(path)} --title {EscapeArg(title)} --content {EscapeArg(content)}{RootArg()}");

        using var doc = JsonDocument.Parse(json);
        var resolved = doc.RootElement.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        return $"Created .docx at {resolved}";
    }

    [McpServerTool, Description("Get metadata about a .docx document (paragraph count, word count, character count).")]
    public static async Task<string> DocxInfo(
        [Description("Path to the .docx file, relative to the restricted root.")] string path)
    {
        var json = await CliRunner.RunAsync($"docx info {EscapeArg(path)}{RootArg()}");

        // Pretty-print the metadata as a human-readable summary
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var paraCount = root.TryGetProperty("paragraphCount", out var pc) ? pc.GetInt32() : 0;
        var wordCount = root.TryGetProperty("wordCount", out var wc) ? wc.GetInt32() : 0;
        var charCount = root.TryGetProperty("charCount", out var cc) ? cc.GetInt32() : 0;

        return $"Paragraphs: {paraCount}\nWords: {wordCount}\nCharacters: {charCount}";
    }
}