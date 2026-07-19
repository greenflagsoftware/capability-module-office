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
        var embedOpt = new Option<bool>("--embed", () => true,
            "Generate vector embeddings for chunks. Set to false to skip embedding for cost control.");

        var cmd = new Command("index", "Build a search index over documents in the restricted root.")
        {
            pathArg, rootOpt, embedOpt,
        };

        cmd.SetHandler(async (string path, string rootOverride, bool embed) =>
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

                IEmbeddingProvider? embeddingProvider = null;
                if (embed)
                {
                    try
                    {
                        embeddingProvider = new OpenAIEmbeddingProvider();
                    }
                    catch (InvalidOperationException ex)
                    {
                        // API key not set — warn and continue without embedding
                        Console.Error.WriteLine($"warning: embedding disabled — {ex.Message}");
                    }
                }

                var summary = await IndexEngine.BuildIndexAsync(
                    root, fullPath, connectionString, embeddingProvider);

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
        }, pathArg, rootOpt, embedOpt);

        return cmd;
    }
}