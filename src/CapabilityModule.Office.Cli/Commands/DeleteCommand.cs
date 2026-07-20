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

                // Snapshot to version store before removal (final version)
                var (version, versionPath) = VersionStore.Snapshot(fullPath, root, path);

                // Remove the file
                File.Delete(fullPath);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["version"] = version,
                    ["versionPath"] = versionPath,
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