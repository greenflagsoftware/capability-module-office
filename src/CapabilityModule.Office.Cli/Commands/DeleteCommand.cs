using System.CommandLine;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class DeleteCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", "Path to the file to delete (relative to the restricted root).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("delete", "Delete a file within the restricted root. The file is snapshotted to the version store before removal, so content is recoverable.")
        {
            pathArg, rootOpt,
        };

        cmd.SetHandler(async (string path, string rootOverride) =>
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

                // Snapshot to version store before removal (final version)
                var (version, versionPath) = VersionStore.Snapshot(fullPath, root, path);

                // Remove the file
                File.Delete(fullPath);

                // Best-effort: also remove any indexed content for this file, so
                // hybrid/semantic search doesn't keep surfacing stale hits for a
                // file that's gone. Not a hard dependency — indexing may not be
                // configured (no OFFICE_DB_CONNECTION) or the file may never have
                // been indexed, and neither is a reason to fail `delete` itself
                // since the file removal above already succeeded.
                bool? indexRemoved = null;
                var connectionString = Environment.GetEnvironmentVariable("OFFICE_DB_CONNECTION");
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    // Match the relative-path form IndexEngine uses when indexing,
                    // rather than trusting the caller's raw path string verbatim.
                    var relativePath = Path.GetRelativePath(root, fullPath);
                    try
                    {
                        indexRemoved = await IndexEngine.RemoveFromIndexAsync(connectionString, relativePath);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"warning: failed to remove '{path}' from the search index: {ex.Message}");
                    }
                }

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["version"] = version,
                    ["versionPath"] = versionPath,
                    ["indexRemoved"] = indexRemoved,
                };
                Console.WriteLine(JsonSerializer.Serialize(result));
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