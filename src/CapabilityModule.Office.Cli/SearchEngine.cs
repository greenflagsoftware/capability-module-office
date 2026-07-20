namespace CapabilityModule.Office.Cli;

internal static class SearchEngine
{
    // Explicit EnumerationOptions rather than the SearchOption.AllDirectories
    // overload: that overload's case sensitivity follows the platform default,
    // which is case-sensitive on Linux (where this module always runs, per
    // Dockerfile) and case-insensitive on Windows — so a filename search that
    // worked in local dev could silently fail in the deployed container.
    private static readonly EnumerationOptions CaseInsensitiveRecursive = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = true,
    };

    public static List<Dictionary<string, object?>> Search(string root, string directory, string pattern)
    {
        var entries = new List<Dictionary<string, object?>>();

        var isGlob = pattern.Contains('*') || pattern.Contains('?');
        var searchPattern = isGlob ? pattern : $"*{pattern}*";

        foreach (var file in Directory.EnumerateFiles(directory, searchPattern, CaseInsensitiveRecursive))
        {
            var info = new FileInfo(file);
            entries.Add(new Dictionary<string, object?>
            {
                ["name"] = info.Name,
                ["path"] = Path.GetRelativePath(root, file),
                ["type"] = "file",
                ["size"] = info.Length,
            });
        }

        return entries;
    }
}