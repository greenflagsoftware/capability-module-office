using System.CommandLine;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class UploadCommand
{
    /// <summary>
    /// Default maximum upload size: 50 MB (in bytes).
    /// Override via <c>OFFICE_MAX_UPLOAD_SIZE</c> environment variable (value in bytes).
    /// </summary>
    internal static readonly long DefaultMaxUploadSize = 50 * 1024 * 1024;

    public Command Command()
    {
        var pathArg = new Argument<string>("path", "Path for the uploaded file (relative to the restricted root).");

        var contentBase64Opt = new Option<string>("--content-base64", () => string.Empty,
            "Base64-encoded content to write. If omitted, reads raw base64 from stdin (content piping).");
        var modeOpt = new Option<string>("--mode", () => "create",
            "Write mode: 'create' (fail if exists) or 'overwrite' (replace existing, versioned).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("upload", "Upload a binary file (base64-encoded) to the restricted root.")
        {
            pathArg, contentBase64Opt, modeOpt, rootOpt,
        };

        cmd.SetHandler((string path, string contentBase64, string mode, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                // Determine write mode
                var effectiveMode = mode.ToLowerInvariant() switch
                {
                    "create" => UploadMode.Create,
                    "overwrite" => UploadMode.Overwrite,
                    _ => throw new ArgumentException($"Invalid mode '{mode}'. Use 'create' or 'overwrite'."),
                };

                // Read base64 content: from --content-base64 or stdin
                if (string.IsNullOrEmpty(contentBase64) && !Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("error: no content provided. Supply --content-base64 or pipe base64 via stdin.");
                    Environment.Exit(4);
                }

                if (string.IsNullOrEmpty(contentBase64) && Console.IsInputRedirected)
                {
                    contentBase64 = Console.In.ReadToEnd();
                }

                // Strip whitespace that may have been introduced by piping
                contentBase64 = contentBase64!.Trim();

                // Decode to get byte count for size check
                byte[] decoded;
                try
                {
                    decoded = Convert.FromBase64String(contentBase64);
                }
                catch (FormatException)
                {
                    Console.Error.WriteLine("error: content is not valid base64.");
                    Environment.Exit(7);
                    return;
                }

                // Enforce max upload size
                var maxSize = GetMaxUploadSize();
                if (decoded.Length > maxSize)
                {
                    Console.Error.WriteLine($"error: upload content exceeds maximum size of {maxSize} bytes ({decoded.Length} bytes provided). Adjust OFFICE_MAX_UPLOAD_SIZE to increase the limit.");
                    Environment.Exit(8);
                    return;
                }

                // Create directory if needed
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                int? version = null;
                string? versionPath = null;

                switch (effectiveMode)
                {
                    case UploadMode.Create:
                        if (File.Exists(fullPath))
                        {
                            Console.Error.WriteLine($"error: file already exists: {path} (use --mode overwrite to replace)");
                            Environment.Exit(5);
                        }
                        break;

                    case UploadMode.Overwrite:
                        if (File.Exists(fullPath))
                        {
                            // Snapshot pre-existing content to version store
                            var snap = VersionStore.Snapshot(fullPath, root, path);
                            version = snap.Version;
                            versionPath = snap.VersionPath;
                        }
                        break;
                }

                File.WriteAllBytes(fullPath, decoded);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["mode"] = effectiveMode.ToString().ToLowerInvariant(),
                    ["bytesWritten"] = decoded.Length,
                };
                if (version.HasValue)
                {
                    result["version"] = version.Value;
                    result["versionPath"] = versionPath;
                }
                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(6);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
        }, pathArg, contentBase64Opt, modeOpt, rootOpt);

        return cmd;
    }

    private enum UploadMode { Create, Overwrite }

    /// <summary>
    /// Returns the configured max upload size from the <c>OFFICE_MAX_UPLOAD_SIZE</c>
    /// environment variable, or the default (50 MB).
    /// </summary>
    internal static long GetMaxUploadSize()
    {
        var env = Environment.GetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE");
        if (!string.IsNullOrWhiteSpace(env) && long.TryParse(env, out var parsed) && parsed > 0)
        {
            return parsed;
        }
        return DefaultMaxUploadSize;
    }
}