namespace AgentDock.Office.Tests;

public class CliRunnerTests
{
    [Fact]
    public void ResolveCliPath_ReturnsNonEmptyPath()
    {
        var path = CliRunner.ResolveCliPath();

        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.Contains("AgentDock.Office.Cli", path, StringComparison.OrdinalIgnoreCase);
    }
}

public class DocxToolsArgumentTests
{
    /// <summary>
    /// Verify that the docx tools at least resolve their types without
    /// throwing (the full integration test requires the CLI binary to be
    /// present at the resolved path, which is tested separately).
    /// </summary>
    [Fact]
    public void DocxTools_TypeIsDiscoverable()
    {
        var type = typeof(Tools.DocxTools);

        Assert.NotNull(type);
        Assert.True(type.IsClass);
        Assert.True(type.IsAbstract && type.IsSealed); // static class
    }
}