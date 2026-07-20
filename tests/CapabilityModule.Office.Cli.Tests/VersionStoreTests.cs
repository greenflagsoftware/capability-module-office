namespace CapabilityModule.Office.Cli.Tests;

public class VersionStoreTests : IDisposable
{
    private readonly string _dir;

    public VersionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "office-cli-version-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Snapshot_CreatesVersionFileInVersionsFolder()
    {
        var filePath = Path.Combine(_dir, "test.docx");
        File.WriteAllText(filePath, "original content");

        var (version, versionPath) = VersionStore.Snapshot(filePath, _dir, "test.docx");

        Assert.Equal(1, version);
        Assert.True(File.Exists(versionPath), "Version file should exist");
        Assert.Contains("_versions", versionPath);
        Assert.EndsWith("test.v1.docx", versionPath);
    }

    [Fact]
    public void Snapshot_IncrementsVersionNumber()
    {
        var filePath = Path.Combine(_dir, "test.docx");
        File.WriteAllText(filePath, "original content");

        var (v1, _) = VersionStore.Snapshot(filePath, _dir, "test.docx");
        // Overwrite the original with new content for the second snapshot
        File.WriteAllText(filePath, "updated content");
        var (v2, path2) = VersionStore.Snapshot(filePath, _dir, "test.docx");

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        Assert.EndsWith("test.v2.docx", path2);
    }

    [Fact]
    public void Snapshot_PreservesOriginalContent()
    {
        var filePath = Path.Combine(_dir, "test.docx");
        File.WriteAllText(filePath, "original content");

        var (_, versionPath) = VersionStore.Snapshot(filePath, _dir, "test.docx");

        Assert.Equal("original content", File.ReadAllText(versionPath));
    }

    [Fact]
    public void Snapshot_WithSubdirectoryMirrorsRelativePath()
    {
        var subdir = Path.Combine(_dir, "subdir");
        Directory.CreateDirectory(subdir);
        var filePath = Path.Combine(subdir, "doc.docx");
        File.WriteAllText(filePath, "content");

        var (_, versionPath) = VersionStore.Snapshot(filePath, _dir, "subdir/doc.docx");

        Assert.Contains("_versions\\subdir", versionPath);
        Assert.EndsWith("doc.v1.docx", versionPath);
    }

    [Fact]
    public void Snapshot_WithDifferentExtension_Works()
    {
        var filePath = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(filePath, "text content");

        var (version, versionPath) = VersionStore.Snapshot(filePath, _dir, "notes.txt");

        Assert.Equal(1, version);
        Assert.EndsWith("notes.v1.txt", versionPath);
    }

    [Fact]
    public void Snapshot_DoesNotOverwriteExistingVersion()
    {
        var filePath = Path.Combine(_dir, "test.docx");
        File.WriteAllText(filePath, "v1 content");

        var (v1, v1Path) = VersionStore.Snapshot(filePath, _dir, "test.docx");
        File.WriteAllText(filePath, "v2 content");
        var (v2, v2Path) = VersionStore.Snapshot(filePath, _dir, "test.docx");

        Assert.Equal(1, v1);
        Assert.Equal(2, v2);
        Assert.Equal("v1 content", File.ReadAllText(v1Path));
        Assert.Equal("v2 content", File.ReadAllText(v2Path));
    }
}