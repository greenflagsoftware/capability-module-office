using System.ComponentModel;
using CapabilityModule.Office.WebApi.Cli;

namespace CapabilityModule.Office.WebApi.Tests;

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
    public async Task RunAsync_HelpCommand_Succeeds()
    {
        // We can't use the string-based overload on the WebApi CliRunner, so use
        // the argument-list overload
        var result = await CliRunner.RunAsync(new List<string> { "--help" });

        Assert.NotEmpty(result);
        Assert.Contains("upload", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_InvalidCommand_ThrowsCliToolException()
    {
        var ex = await Assert.ThrowsAsync<CliToolException>(() =>
            CliRunner.RunAsync(new List<string> { "nonexistent-command" }));

        Assert.NotEqual(0, ex.ExitCode);
        Assert.NotEmpty(ex.Stderr);
        Assert.NotEmpty(ex.CliCommand);
    }

    [Fact]
    public async Task RunAsync_ShortTimeout_ThrowsCliTimeoutException()
    {
        var cliPath = CliRunner.ResolveCliPath();

        var ex = await Assert.ThrowsAsync<CliTimeoutException>(() =>
            CliRunner.RunAsync(cliPath, new List<string> { "--help" }, TimeSpan.FromTicks(1), CancellationToken.None));

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(ex.CliCommand);
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