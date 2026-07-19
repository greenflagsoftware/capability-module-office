namespace AgentDock.Office.Cli.Tests;

public class SearchEngineTests : IDisposable
{
    private readonly string _root;

    public SearchEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "office-cli-search-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        // Create a test directory structure
        Directory.CreateDirectory(Path.Combine(_root, "subdir"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));

        File.WriteAllText(Path.Combine(_root, "readme.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "world");
        File.WriteAllText(Path.Combine(_root, "script.js"), "console.log('hi');");
        File.WriteAllText(Path.Combine(_root, "subdir", "data.txt"), "data");
        File.WriteAllText(Path.Combine(_root, "subdir", "report.docx"), "report");
        File.WriteAllText(Path.Combine(_root, "docs", "summary.docx"), "summary");
        File.WriteAllText(Path.Combine(_root, "docs", "draft.txt"), "draft");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Search_SubstringPattern_FindsMatchingFiles()
    {
        var entries = SearchEngine.Search(_root, _root, "readme");

        Assert.Single(entries);
        Assert.Equal("readme.txt", entries[0]["name"]);
    }

    [Fact]
    public void Search_GlobPattern_FindsMatchingFiles()
    {
        var entries = SearchEngine.Search(_root, _root, "*.txt");

        Assert.Equal(4, entries.Count);
        Assert.All(entries, e => Assert.EndsWith(".txt", (string)e["name"]!));
    }

    [Fact]
    public void Search_GlobPattern_Recursive_FindsFilesInSubdirectories()
    {
        var entries = SearchEngine.Search(_root, _root, "*.docx");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.EndsWith(".docx", (string)e["name"]!));
    }

    [Fact]
    public void Search_SubstringPattern_Recursive_FindsFilesInSubdirectories()
    {
        var entries = SearchEngine.Search(_root, _root, "report");

        Assert.Single(entries);
        Assert.Equal("report.docx", entries[0]["name"]);
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmptyEntries()
    {
        var entries = SearchEngine.Search(_root, _root, "nonexistent");

        Assert.Empty(entries);
    }

    [Fact]
    public void Search_EachEntry_HasNamePathTypeAndSize()
    {
        var entries = SearchEngine.Search(_root, _root, "readme.txt");

        Assert.Single(entries);
        var entry = entries[0];
        Assert.Contains("name", entry);
        Assert.Contains("path", entry);
        Assert.Contains("type", entry);
        Assert.Contains("size", entry);
        Assert.Equal("file", entry["type"]);
    }

    [Fact]
    public void Search_Pattern_IsCaseInsensitive()
    {
        var entries = SearchEngine.Search(_root, _root, "README");

        Assert.Single(entries);
        Assert.Equal("readme.txt", entries[0]["name"]);
    }

    [Fact]
    public void Search_ScopedToSubdirectory_OnlyFindsFilesUnderThatPath()
    {
        var subdirPath = Path.Combine(_root, "subdir");
        var entries = SearchEngine.Search(_root, subdirPath, "*.*");

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            var path = (string)e["path"]!;
            Assert.StartsWith("subdir", path);
        });
    }
}