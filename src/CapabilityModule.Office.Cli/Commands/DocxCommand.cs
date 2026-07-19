using System.CommandLine;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class DocxCommand
{
    public Command Command()
    {
        var cmd = new Command("docx", "Work with Word (.docx) documents — read text, create documents, and inspect metadata.");

        cmd.AddCommand(ReadSubCommand());
        cmd.AddCommand(CreateSubCommand());
        cmd.AddCommand(InfoSubCommand());

        return cmd;
    }

    private static Command ReadSubCommand()
    {
        var pathArg = new Argument<string>("path", "Path to the .docx file (relative to the restricted root).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("read", "Extract plain text from a .docx document.")
        {
            pathArg, rootOpt,
        };

        cmd.SetHandler((string path, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"error: file not found: {path}");
                    Environment.Exit(1);
                }

                var text = DocxEngine.ReadText(fullPath);
                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["content"] = text,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
        }, pathArg, rootOpt);

        return cmd;
    }

    private static Command CreateSubCommand()
    {
        var pathArg = new Argument<string>("path", "Path for the new .docx file (relative to the restricted root).");
        var titleOpt = new Option<string>("--title", () => "Document", "Document title (used as the first heading).");
        var contentOpt = new Option<string>("--content", () => string.Empty,
            "Text content for the document body. If omitted, reads from stdin (content piping).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("create", "Create a new .docx document from text content.")
        {
            pathArg, titleOpt, contentOpt, rootOpt,
        };

        cmd.SetHandler((string path, string title, string content, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                if (File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"error: file already exists: {path} (delete it first or use a different path)");
                    Environment.Exit(5);
                }

                // If no content provided via --content, try stdin (content piping)
                if (string.IsNullOrEmpty(content) && Console.IsInputRedirected)
                {
                    content = Console.In.ReadToEnd();
                }

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                DocxEngine.Create(fullPath, title, content ?? "");

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["title"] = title,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
        }, pathArg, titleOpt, contentOpt, rootOpt);

        return cmd;
    }

    private static Command InfoSubCommand()
    {
        var pathArg = new Argument<string>("path", "Path to the .docx file (relative to the restricted root).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("info", "Show metadata about a .docx document (paragraphs, word count, etc.).")
        {
            pathArg, rootOpt,
        };

        cmd.SetHandler((string path, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                if (!File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"error: file not found: {path}");
                    Environment.Exit(1);
                }

                var info = DocxEngine.GetInfo(fullPath);
                info["path"] = path;
                info["resolved"] = fullPath;
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
        }, pathArg, rootOpt);

        return cmd;
    }
}