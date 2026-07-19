namespace AgentDock.Office.Cli;

internal static class SearchEngine
{
    public static List<Dictionary<string, object?>> Search(string root, string directory, string pattern)
    {
        var entries = new List<Dictionary<string, object?>>();

        var isGlob = pattern.Contains('*') || pattern.Contains('?');
        var searchPattern = isGlob ? pattern : $"*{pattern}*";

        foreach (var file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories))
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