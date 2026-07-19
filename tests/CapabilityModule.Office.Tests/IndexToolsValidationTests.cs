namespace CapabilityModule.Office.Tests;

public class IndexToolsValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IndexSearch_NullOrEmptyQuery_ThrowsArgumentException(string? invalidQuery)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.IndexTools.IndexSearch(invalidQuery!));

        Assert.Contains("query", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexSearch_WhitespaceQuery_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tools.IndexTools.IndexSearch("   "));

        Assert.Contains("query", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a valid query passes argument validation and reaches the
    /// CLI layer (which will fail with a DB-not-configured error in test
    /// environments without Postgres — a distinct error from validation).
    /// </summary>
    [Fact]
    public async Task IndexSearch_ValidQuery_ReachesCliLayer_NotValidationError()
    {
        // Should not throw ArgumentException — validation passes.
        // The CLI call will fail with some other error (no DB, no CLI binary, etc.)
        // which is expected — the key is it's NOT an ArgumentException.
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexSearch("test query"));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    /// <summary>
    /// Verifies path is optional — passing null should not trigger validation error.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IndexSearch_PathOptional_ReachesCliLayer_NotValidationError(string? path)
    {
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexSearch("test query", path));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    /// <summary>
    /// Verifies valid limit values pass validation (no ArgumentException).
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task IndexSearch_ValidLimits_NotValidationError(int validLimit)
    {
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexSearch("test query", null, validLimit));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    [Fact]
    public async Task IndexBuild_DefaultArgs_ReachesCliLayer_NotValidationError()
    {
        // Should not throw ArgumentException — all args have defaults.
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexBuild());

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    [Fact]
    public async Task IndexBuild_WithEmbedFalse_ReachesCliLayer_NotValidationError()
    {
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexBuild(null, false));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    [Fact]
    public async Task IndexBuild_WithEmbedTrue_ReachesCliLayer_NotValidationError()
    {
        var ex = await Record.ExceptionAsync(() =>
            Tools.IndexTools.IndexBuild(null, true));

        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }
}