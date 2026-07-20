using System.CommandLine;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class IndexCommand
{
    public Command Command()
    {
        var cmd = new Command("index", "Index documents and search the index. Use 'index build' to (re)build the index, 'index search' to query it.");

        cmd.AddCommand(BuildSubCommand());
        cmd.AddCommand(SearchSubCommand());

        return cmd;
    }

    private static Command BuildSubCommand()
    {
        var pathArg = new Argument<string>("path", () => ".",
            "Directory to index (relative to the restricted root). Defaults to the root itself.");
        var rootOpt = SharedOptions.RootOption();
        var embedOpt = new Option<bool>("--embed", () => true,
            "Generate vector embeddings for chunks. Set to false to skip embedding for cost control.");

        var cmd = new Command("build", "Build a search index over documents in the restricted root.")
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
                        embeddingProvider = EmbeddingProviderFactory.Create();
                    }
                    catch (InvalidOperationException ex)
                    {
                        // Provider unset/misconfigured (e.g. no API key) — warn and continue without embedding
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

    private static Command SearchSubCommand()
    {
        var queryArg = new Argument<string>("query", "Free-text search query for semantic/hybrid search over indexed documents.");
        var pathArg = new Argument<string>("path", () => ".",
            "Subdirectory to scope the search under (relative to the restricted root). Defaults to the root.");
        var rootOpt = SharedOptions.RootOption();
        var limitOpt = new Option<int>("--limit", () => 10,
            $"Maximum number of results to return (max {IndexSearchEngine.MaxLimit}).");

        var cmd = new Command("search", "Search indexed document content using hybrid (vector + keyword) search.")
        {
            queryArg, pathArg, rootOpt, limitOpt,
        };

        cmd.SetHandler(async (string query, string path, string rootOverride, int limit) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    Console.Error.WriteLine("error: query must not be empty.");
                    Environment.Exit(1);
                }

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
                    Console.Error.WriteLine("error: OFFICE_DB_CONNECTION environment variable is not set. Searching requires a Postgres database.");
                    Environment.Exit(4);
                }

                IEmbeddingProvider? embeddingProvider = null;
                try
                {
                    embeddingProvider = EmbeddingProviderFactory.Create();
                }
                catch (InvalidOperationException ex)
                {
                    Console.Error.WriteLine($"error: {ex.Message}. A working embedding provider is required for search.");
                    Environment.Exit(5);
                    return;
                }

                var scope = path != "." ? path : null;

                var searchResults = await IndexSearchEngine.SearchAsync(
                    query, connectionString, embeddingProvider, scope, limit);

                var resultEntries = new List<Dictionary<string, object?>>();
                foreach (var r in searchResults.Results)
                {
                    var entry = new Dictionary<string, object?>
                    {
                        ["documentPath"] = r.DocumentPath,
                        ["chunkIndex"] = r.ChunkIndex,
                        ["text"] = r.Text,
                        ["score"] = Math.Round(r.Score, 6),
                        ["vectorScore"] = Math.Round(r.VectorScore, 6),
                        ["keywordScore"] = Math.Round(r.KeywordScore, 6),
                    };
                    if (r.HeadingPath is { Count: > 0 })
                    {
                        entry["headingPath"] = r.HeadingPath;
                    }
                    resultEntries.Add(entry);
                }

                var result = new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["totalResults"] = searchResults.TotalResults,
                    ["limit"] = searchResults.Limit,
                    ["entries"] = resultEntries,
                };

                Console.WriteLine(JsonSerializer.Serialize(result));
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(2);
            }
            catch (InvalidOperationException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.Exit(3);
            }
        }, queryArg, pathArg, rootOpt, limitOpt);

        return cmd;
    }
}