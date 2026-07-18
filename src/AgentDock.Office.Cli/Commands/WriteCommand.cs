using System.CommandLine;

namespace AgentDock.Office.Cli.Commands;

internal sealed class WriteCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", "Path to the file to write (relative to the restricted root).");

        var modeOpt = new Option<string>("--mode", () => "create",
            "Write mode: 'create' (fail if exists), 'overwrite' (replace), or 'append'.");
        var contentOpt = new Option<string>("--content", () => string.Empty,
            "Text content to write. If omitted, reads from stdin (content piping).");
        var rootOpt = new Option<string>("--root", () => string.Empty,
            "Override the restricted root directory. Defaults to $OFFICE_CLI_ROOT or the current directory.");

        var cmd = new Command("write", "Write text content to a file within the restricted root.")
        {
            pathArg, modeOpt, contentOpt, rootOpt,
        };

        cmd.SetHandler((string path, string mode, string content, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                // Determine write mode
                var effectiveMode = mode.ToLowerInvariant() switch
                {
                    "create" => WriteMode.Create,
                    "overwrite" => WriteMode.Overwrite,
                    "append" => WriteMode.Append,
                    _ => throw new ArgumentException($"Invalid mode '{mode}'. Use 'create', 'overwrite', or 'append'."),
                };

                // If no content provided via --content, try reading from stdin (piped input)
                if (string.IsNullOrEmpty(content) && !Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("error: no content provided. Supply --content or pipe input via stdin.");
                    Environment.Exit(4);
                }

                if (string.IsNullOrEmpty(content) && Console.IsInputRedirected)
                {
                    content = Console.In.ReadToEnd();
                }

                // Create directory if needed
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                switch (effectiveMode)
                {
                    case WriteMode.Create:
                        if (File.Exists(fullPath))
                        {
                            Console.Error.WriteLine($"error: file already exists: {path} (use --mode overwrite to replace)");
                            Environment.Exit(5);
                        }
                        File.WriteAllText(fullPath, content);
                        break;

                    case WriteMode.Overwrite:
                        File.WriteAllText(fullPath, content);
                        break;

                    case WriteMode.Append:
                        File.AppendAllText(fullPath, content);
                        break;
                }

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["mode"] = effectiveMode.ToString().ToLowerInvariant(),
                    ["bytesWritten"] = System.Text.Encoding.UTF8.GetByteCount(content),
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
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
        }, pathArg, modeOpt, contentOpt, rootOpt);

        return cmd;
    }

    private enum WriteMode { Create, Overwrite, Append }
}