namespace AgentDock.Office.Tests;

public class SearchToolsValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_NullOrEmptyPattern_ThrowsArgumentException(string? invalidPattern)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.SearchTools.Search(invalidPattern!));

        Assert.Contains("pattern", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithPath_NullPathIsAllowed(string? path)
    {
        // null/empty path should work — it uses the default (root)
        // This should not throw ArgumentException for path since it's optional
        var ex = await Record.ExceptionAsync(() => Tools.SearchTools.Search("*.txt", path));
        Assert.Null(ex);
    }
}