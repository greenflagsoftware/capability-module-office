using System.CommandLine;

namespace AgentDock.Office.Cli.Commands;

internal sealed class ListCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", () => ".",
            "Directory to list (relative to the restricted root). Defaults to the root itself.");
        var rootOpt = new Option<string>("--root", () => string.Empty,
            "Override the restricted root directory. Defaults to $OFFICE_CLI_ROOT or the current directory.");

        var cmd = new Command("list", "List entries in a directory within the restricted root.")
        {
            pathArg, rootOpt,
        };

        cmd.SetHandler((string path, string rootOverride) =>
        {
            try
            {
                var root = !string.IsNullOrWhiteSpace(rootOverride)
                    ? Path.GetFullPath(rootOverride)
                    : PathSecurity.ResolveRoot();

                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                if (!Directory.Exists(fullPath))
                {
                    Console.Error.WriteLine($"error: directory not found: {path}");
                    Environment.Exit(1);
                }

                var entries = new List<Dictionary<string, object?>>();

                foreach (var dir in Directory.GetDirectories(fullPath))
                {
                    entries.Add(new Dictionary<string, object?>
                    {
                        ["name"] = Path.GetFileName(dir),
                        ["path"] = Path.GetRelativePath(root, dir),
                        ["type"] = "directory",
                    });
                }

                foreach (var file in Directory.GetFiles(fullPath))
                {
                    var info = new FileInfo(file);
                    entries.Add(new Dictionary<string, object?>
                    {
                        ["name"] = Path.GetFileName(file),
                        ["path"] = Path.GetRelativePath(root, file),
                        ["type"] = "file",
                        ["size"] = info.Length,
                    });
                }

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["entries"] = entries,
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
}