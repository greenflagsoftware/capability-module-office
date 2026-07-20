using System.ComponentModel;

namespace CapabilityModule.Office.Tests;

public class CliRunnerTests
{
    [Fact]
    public void ResolveCliPath_ReturnsNonEmptyPath()
    {
        var path = CliRunner.ResolveCliPath();

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.Contains("CapabilityModule.Office.Cli", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DefaultTimeout_Is30Seconds()
    {
        Assert.Equal(30, CliRunner.DefaultTimeout.TotalSeconds);
    }

    [Fact]
    public async Task RunAsync_BinaryNotFound_ThrowsWin32Exception()
    {
        var ex = await Assert.ThrowsAsync<Win32Exception>(() =>
            CliRunner.RunAsync("/nonexistent/path/CapabilityModule.Office.Cli.exe", "--help",
                TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Contains("trying to start process", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_HelpCommand_Succeeds()
    {
        var result = await CliRunner.RunAsync("--help");

        Assert.NotEmpty(result);
        Assert.Contains("docx", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_InvalidCommand_ThrowsCliToolException()
    {
        var ex = await Assert.ThrowsAsync<CliToolException>(() =>
            CliRunner.RunAsync("nonexistent-command"));

        Assert.NotEqual(0, ex.ExitCode);
        Assert.NotEmpty(ex.Stderr);
        Assert.NotEmpty(ex.CliCommand);
    }

    [Fact]
    public async Task RunAsync_ReadNonexistentFile_ThrowsCliToolExceptionWithExitCode1()
    {
        var ex = await Assert.ThrowsAsync<CliToolException>(() =>
            CliRunner.RunAsync("docx read nonexistent.docx"));

        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("not found", ex.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_PathTraversal_ThrowsCliToolExceptionWithExitCode2()
    {
        var ex = await Assert.ThrowsAsync<CliToolException>(() =>
            CliRunner.RunAsync("docx read ../outside.docx"));

        Assert.Equal(2, ex.ExitCode);
        Assert.Contains("outside", ex.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ShortTimeout_ThrowsCliTimeoutException()
    {
        // Use a microsecond-scale timeout to reliably trigger a timeout.
        // The CLI process starts, but the timeout fires almost immediately.
        var cliPath = CliRunner.ResolveCliPath();

        var ex = await Assert.ThrowsAsync<CliTimeoutException>(() =>
            CliRunner.RunAsync(cliPath, "read /dev/null", TimeSpan.FromTicks(1), CancellationToken.None));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ex.CliCommand);
    }

    [Fact]
    public async Task RunAsync_CancellationToken_ThrowsCliTimeoutException()
    {
        var cliPath = CliRunner.ResolveCliPath();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        var ex = await Assert.ThrowsAsync<CliTimeoutException>(() =>
            CliRunner.RunAsync(cliPath, "--help", TimeSpan.FromSeconds(30), cts.Token));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class CliToolExceptionTests
{
    [Fact]
    public void CliToolException_StoresProperties()
    {
        var ex = new CliToolException(42, "something broke", "cli.exe do-thing");

        Assert.Equal(42, ex.ExitCode);
        Assert.Equal("something broke", ex.Stderr);
        Assert.Equal("cli.exe do-thing", ex.CliCommand);
        Assert.Contains("42", ex.Message);
        Assert.Contains("something broke", ex.Message);
    }
}

public class CliTimeoutExceptionTests
{
    [Fact]
    public void CliTimeoutException_StoresProperties()
    {
        var ex = new CliTimeoutException(TimeSpan.FromSeconds(5), "cli.exe hang");

        Assert.Equal(5, ex.Timeout.TotalSeconds);
        Assert.Equal("cli.exe hang", ex.CliCommand);
        Assert.Contains("5", ex.Message);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class CliMalformedOutputExceptionTests
{
    [Fact]
    public void CliMalformedOutputException_StoresProperties()
    {
        var inner = new FormatException("bad format");
        var ex = new CliMalformedOutputException(
            "JSON parse failed", "{bad json}", "cli.exe do-thing", inner);

        Assert.Equal("{bad json}", ex.RawOutput);
        Assert.Equal("cli.exe do-thing", ex.CliCommand);
        Assert.Same(inner, ex.InnerException);
        Assert.Contains("JSON", ex.Message);
    }
}

public class DocxToolsValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocxRead_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.DocxTools.DocxRead(invalidPath!));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocxCreate_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.DocxTools.DocxCreate(invalidPath!, "title", "content"));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocxInfo_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.DocxTools.DocxInfo(invalidPath!));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocxReplace_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.DocxTools.DocxReplace(invalidPath!, "find", "replace"));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DocxReplace_NullOrEmptyFind_ThrowsArgumentException(string? invalidFind)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.DocxTools.DocxReplace("test.docx", invalidFind!, "replace"));

        Assert.Contains("find", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocxReplace_ValidArgs_ReachesCliLayer_NotValidationError()
    {
        // Should not throw ArgumentException — validation passes.
        // The CLI call will fail with some other error (file not found, etc.)
        var ex = await Record.ExceptionAsync(() =>
            Tools.DocxTools.DocxReplace("test.docx", "find", "replace"));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }
}

public class FileToolsValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadFile_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.FileTools.UploadFile(invalidPath!, "dGVzdA=="));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadFile_NullOrEmptyContent_ThrowsArgumentException(string? invalidContent)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.FileTools.UploadFile("test.bin", invalidContent!));

        Assert.Contains("content", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadFile_ValidArgs_ReachesCliLayer_NotValidationError()
    {
        // Should not throw ArgumentException — validation passes.
        // The CLI call may succeed or fail with some other error (file system, etc.)
        // Either way, it's not a validation error.
        var ex = await Record.ExceptionAsync(() =>
            Tools.FileTools.UploadFile("test.bin", "dGVzdA=="));

        // If it failed, it must not be an ArgumentException (validation error)
        if (ex != null)
        {
            Assert.IsNotType<ArgumentException>(ex);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteFile_NullOrEmptyPath_ThrowsArgumentException(string? invalidPath)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.FileTools.DeleteFile(invalidPath!));

        Assert.Contains("path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteFile_ValidArgs_ReachesCliLayer_NotValidationError()
    {
        var ex = await Record.ExceptionAsync(() =>
            Tools.FileTools.DeleteFile("test.txt"));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }
}