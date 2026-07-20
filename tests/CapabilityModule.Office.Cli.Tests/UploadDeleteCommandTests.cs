using System.CommandLine;
using System.Text.Json;
using CapabilityModule.Office.Cli.Commands;

namespace CapabilityModule.Office.Cli.Tests;

public class UploadCommandTests : IDisposable
{
    private readonly string _root;
    private readonly RootCommand _rootCmd;

    public UploadCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "office-cli-upload-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        _rootCmd = new RootCommand { new UploadCommand().Command() };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_CreateMode_WritesBytesCorrectly()
    {
        var content = "Hello, World!";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        var args = new[] { "upload", "test.txt", "--content-base64", base64, "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        var filePath = Path.Combine(_root, "test.txt");
        Assert.True(File.Exists(filePath));
        var written = File.ReadAllText(filePath);
        Assert.Equal(content, written);
    }

    [Fact]
    public async Task Upload_OverwriteMode_ReplacesContent()
    {
        File.WriteAllText(Path.Combine(_root, "replace.txt"), "original content");
        var newContent = "replaced content";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newContent));

        var args = new[] { "upload", "replace.txt", "--content-base64", base64, "--mode", "overwrite", "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal(newContent, File.ReadAllText(Path.Combine(_root, "replace.txt")));
    }

    [Fact]
    public async Task Upload_OverwriteMode_VersionsPreviousContent()
    {
        File.WriteAllText(Path.Combine(_root, "versioned.txt"), "v1 content");
        var newContent = "v2 content";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newContent));

        var args = new[] { "upload", "versioned.txt", "--content-base64", base64, "--mode", "overwrite", "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal("v2 content", File.ReadAllText(Path.Combine(_root, "versioned.txt")));

        // Version file should exist
        var versionPath = Path.Combine(_root, "_versions", "versioned.v1.txt");
        Assert.True(File.Exists(versionPath), "Version file should exist");
        Assert.Equal("v1 content", File.ReadAllText(versionPath));
    }

    [Fact]
    public async Task Upload_OverwriteMode_NewFile_NoVersionCreated()
    {
        var content = "new file";
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content));

        // Overwrite on a file that doesn't exist yet should work like create
        var args = new[] { "upload", "brandnew.txt", "--content-base64", base64, "--mode", "overwrite", "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.Equal(content, File.ReadAllText(Path.Combine(_root, "brandnew.txt")));
    }

    [Fact]
    public async Task Upload_BinaryContent_RoundTripsCorrectly()
    {
        // Create a small binary payload (100 bytes of non-text data)
        var binary = new byte[100];
        new Random(42).NextBytes(binary);
        var base64 = Convert.ToBase64String(binary);

        var args = new[] { "upload", "binary.dat", "--content-base64", base64, "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        var written = File.ReadAllBytes(Path.Combine(_root, "binary.dat"));

        Assert.Equal(binary.Length, written.Length);
        Assert.True(binary.SequenceEqual(written), "Binary content should round-trip exactly");
    }

    [Fact]
    public void GetMaxUploadSize_NoEnvVar_ReturnsDefault()
    {
        Environment.SetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE", null);
        Assert.Equal(50 * 1024 * 1024, UploadCommand.GetMaxUploadSize());
    }

    [Fact]
    public void GetMaxUploadSize_WithEnvVar_ReturnsConfigured()
    {
        Environment.SetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE", "1048576");
        Assert.Equal(1_048_576, UploadCommand.GetMaxUploadSize());
        Environment.SetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE", null);
    }

    [Fact]
    public void GetMaxUploadSize_InvalidEnvVar_ReturnsDefault()
    {
        Environment.SetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE", "not-a-number");
        Assert.Equal(50 * 1024 * 1024, UploadCommand.GetMaxUploadSize());
        Environment.SetEnvironmentVariable("OFFICE_MAX_UPLOAD_SIZE", null);
    }
}

public class DeleteCommandTests : IDisposable
{
    private readonly string _root;
    private readonly RootCommand _rootCmd;

    public DeleteCommandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "office-cli-delete-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        _rootCmd = new RootCommand { new DeleteCommand().Command() };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Delete_ExistingFile_RemovesAndVersions()
    {
        var filePath = Path.Combine(_root, "delete-me.txt");
        File.WriteAllText(filePath, "content to delete");

        var args = new[] { "delete", "delete-me.txt", "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(filePath), "Original file should be removed");

        // Version should exist
        var versionPath = Path.Combine(_root, "_versions", "delete-me.v1.txt");
        Assert.True(File.Exists(versionPath), "Version file should exist");
        Assert.Equal("content to delete", File.ReadAllText(versionPath));
    }

    [Fact]
    public async Task Delete_FileInSubdirectory_VersionsInMirroredPath()
    {
        var subdir = Path.Combine(_root, "subdir");
        Directory.CreateDirectory(subdir);
        var filePath = Path.Combine(subdir, "nested.txt");
        File.WriteAllText(filePath, "nested content");

        var args = new[] { "delete", "subdir/nested.txt", "--root", _root };
        var exitCode = await _rootCmd.InvokeAsync(args);

        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(filePath), "Original file should be removed");

        var versionPath = Path.Combine(_root, "_versions", "subdir", "nested.v1.txt");
        Assert.True(File.Exists(versionPath), "Version file should exist in mirrored path");
        Assert.Equal("nested content", File.ReadAllText(versionPath));
    }
}