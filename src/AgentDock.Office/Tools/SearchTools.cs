using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AgentDock.Office.Tools;

[McpServerToolType]
public static class SearchTools
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

    private static void ValidatePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be null or empty.", paramName);
        }
    }

    private static async Task<JsonDocument> CallCliAsync(IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        string json;
        try
        {
            json = await CliRunner.RunAsync(arguments, timeout);
        }
        catch (CliToolException ex)
        {
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

    [McpServerTool, Description("Search for files by name or path pattern within the restricted root, recursively. Supports glob patterns (e.g. *.txt, report*) and substring matching. Returns matching file names, paths, and sizes.")]
    public static async Task<string> Search(
        [Description("Filename or path pattern to search for (substring or glob, e.g. *.txt, report, report*).")] string pattern,
        [Description("Directory to search under, relative to the restricted root. Defaults to the root directory.")] string? path = null)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Pattern must not be null or empty.", nameof(pattern));
        }

        var args = BuildArgs("search", pattern);
        if (!string.IsNullOrWhiteSpace(path))
        {
            args.Add(path);
        }

        using var doc = await CallCliAsync(args);

        var root = doc.RootElement;
        var entries = root.TryGetProperty("entries", out var e) ? e : default;

        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
        {
            return "No matching files found.";
        }

        var results = new List<string>();
        foreach (var entry in entries.EnumerateArray())
        {
            var name = entry.TryGetProperty("name", out var n) ? n.GetString() : "";
            var filePath = entry.TryGetProperty("path", out var p) ? p.GetString() : "";
            var size = entry.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            results.Add($"  {filePath} ({FormatSize(size)})");
        }

        return $"Found {entries.GetArrayLength()} matching file(s):\n" + string.Join("\n", results);
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
        };
    }
}