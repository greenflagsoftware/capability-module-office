using System.CommandLine;

namespace AgentDock.Office.Cli.Commands;

internal sealed class ReadCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", "Path to the file to read (relative to the restricted root).");
        var rootOpt = new Option<string>("--root", () => string.Empty,
            "Override the restricted root directory. Defaults to $OFFICE_CLI_ROOT or the current directory.");

        var cmd = new Command("read", "Read the contents of a text file within the restricted root.")
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

                var content = File.ReadAllText(fullPath);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["content"] = content,
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