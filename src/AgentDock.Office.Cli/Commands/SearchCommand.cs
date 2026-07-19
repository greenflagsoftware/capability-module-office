using System.CommandLine;

namespace AgentDock.Office.Cli.Commands;

internal sealed class SearchCommand
{
    public Command Command()
    {
        var patternArg = new Argument<string>("pattern",
            "Filename or path pattern to search for (substring or glob, e.g. *.txt, report).");
        var pathArg = new Argument<string>("path", () => ".",
            "Directory to search under (relative to the restricted root). Defaults to the root itself.");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("search", "Find files by name/path pattern within the restricted root, recursively.")
        {
            patternArg, pathArg, rootOpt,
        };

        cmd.SetHandler((string pattern, string path, string rootOverride) =>
        {
            try
            {
                var root = PathSecurity.EffectiveRoot(rootOverride);
                var fullPath = PathSecurity.ResolveWithinRoot(root, path);

                if (!Directory.Exists(fullPath))
                {
                    Console.Error.WriteLine($"error: directory not found: {path}");
                    Environment.Exit(1);
                }

                var entries = SearchEngine.Search(root, fullPath, pattern);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["pattern"] = pattern,
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
        }, patternArg, pathArg, rootOpt);

        return cmd;
    }
}