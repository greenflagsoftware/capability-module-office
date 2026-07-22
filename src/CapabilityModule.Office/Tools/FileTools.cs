using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CapabilityModule.Office.Tools;

/// <summary>
/// MCP tools that shell out to the CapabilityModule.Office.CLI for file
/// management operations — uploading and deleting files.
/// </summary>
[McpServerToolType]
public static class FileTools
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

    [McpServerTool, Description("Upload a binary file (base64-encoded) to the restricted root. Decodes the base64 content and writes the bytes to the specified path. Use --mode create (default, fails if exists) or --mode overwrite (replaces existing, versioned). Max upload size: 50 MB (configurable via OFFICE_MAX_UPLOAD_SIZE).")]
    public static async Task<string> UploadFile(
        [Description("Path for the uploaded file, relative to the restricted root.")] string path,
        [Description("Base64-encoded content of the file to upload.")] string contentBase64,
        [Description("Write mode: 'create' (fail if exists) or 'overwrite' (replace existing, versioned). Defaults to 'create'.")] string? mode = "create")
    {
        ValidatePath(path, nameof(path));

        if (string.IsNullOrWhiteSpace(contentBase64))
        {
            throw new ArgumentException("Content must not be null or empty.", nameof(contentBase64));
        }

        var args = BuildArgs("upload", path, "--content-base64", contentBase64);
        if (!string.IsNullOrWhiteSpace(mode) && !string.Equals(mode, "create", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--mode");
            args.Add(mode!);
        }

        using var doc = await CallCliAsync(args, CliRunner.DefaultTimeout);

        var root = doc.RootElement;
        var resolved = root.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        var bytesWritten = root.TryGetProperty("bytesWritten", out var bw) ? bw.GetInt64() : 0;

        var msg = $"Uploaded {bytesWritten} bytes to {resolved}";

        if (root.TryGetProperty("version", out var v) && v.GetInt32() is int version and > 0)
        {
            var versionPath = root.TryGetProperty("versionPath", out var vp) ? vp.GetString() : "";
            msg += $". Previous version saved as v{version} at {versionPath}";
        }

        return msg;
    }

    [McpServerTool, Description("Download a file's raw bytes (base64-encoded) from the restricted root. Use this to retrieve a file's exact original content, as opposed to docx_read's extracted plain text.")]
    public static async Task<string> DownloadFile(
        [Description("Path to the file to download, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync(BuildArgs("download", path), CliRunner.DefaultTimeout);

        var root = doc.RootElement;
        var resolved = root.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        var filename = root.TryGetProperty("filename", out var fn) ? fn.GetString() : Path.GetFileName(path);
        var sizeBytes = root.TryGetProperty("sizeBytes", out var sb) ? sb.GetInt64() : 0;
        var contentBase64 = root.TryGetProperty("contentBase64", out var cb) ? cb.GetString() : "";

        return JsonSerializer.Serialize(new
        {
            path,
            resolved,
            filename,
            sizeBytes,
            contentBase64,
        });
    }

    [McpServerTool, Description("Delete a file within the restricted root. The file is snapshotted to the version store before removal, so the content is recoverable from the version store.")]
    public static async Task<string> DeleteFile(
        [Description("Path to the file to delete, relative to the restricted root.")] string path)
    {
        ValidatePath(path, nameof(path));

        using var doc = await CallCliAsync(BuildArgs("delete", path), CliRunner.DefaultTimeout);

        var root = doc.RootElement;
        var resolved = root.TryGetProperty("resolved", out var r) ? r.GetString() : path;
        var version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
        var versionPath = root.TryGetProperty("versionPath", out var vp) ? vp.GetString() : "";

        var msg = $"Deleted {resolved}. Content preserved as version {version} at {versionPath}.";
        if (root.TryGetProperty("indexRemoved", out var ir) && ir.ValueKind != JsonValueKind.Null && ir.GetBoolean())
        {
            msg += " Also removed from the search index.";
        }

        return msg;
    }
}