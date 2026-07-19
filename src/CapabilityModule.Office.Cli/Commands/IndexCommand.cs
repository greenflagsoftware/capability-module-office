using System.CommandLine;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class IndexCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", () => ".",
            "Directory to index (relative to the restricted root). Defaults to the root itself.");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("index", "Build a search index over documents in the restricted root.")
        {
            pathArg, rootOpt,
        };

        cmd.SetHandler(async (string path, string rootOverride) =>
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

                var connectionString = Environment.GetEnvironmentVariable("OFFICE_DB_CONNECTION");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.Error.WriteLine("error: OFFICE_DB_CONNECTION environment variable is not set. Indexing requires a Postgres database.");
                    Environment.Exit(4);
                }

                var summary = await IndexEngine.BuildIndexAsync(root, fullPath, connectionString);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                };
                foreach (var kvp in summary.ToDictionary())
                {
                    result[kvp.Key] = kvp.Value;
                }

                Console.WriteLine(JsonSerializer.Serialize(result));

                if (summary.FilesWithErrors > 0)
                {
                    Environment.Exit(3);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(1);
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