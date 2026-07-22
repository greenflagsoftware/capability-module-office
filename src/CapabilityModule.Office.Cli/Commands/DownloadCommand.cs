using System.CommandLine;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Commands;

internal sealed class DownloadCommand
{
    public Command Command()
    {
        var pathArg = new Argument<string>("path", "Path to the file to download (relative to the restricted root).");
        var rootOpt = SharedOptions.RootOption();

        var cmd = new Command("download", "Read a file's raw bytes (base64-encoded) within the restricted root.")
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
                    return;
                }

                var bytes = File.ReadAllBytes(fullPath);

                var result = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["resolved"] = fullPath,
                    ["filename"] = Path.GetFileName(fullPath),
                    ["sizeBytes"] = bytes.LongLength,
                    ["contentBase64"] = Convert.ToBase64String(bytes),
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
