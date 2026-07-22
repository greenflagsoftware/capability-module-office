namespace CapabilityModule.Office.Cli.Tests;

public class IndexSearchEngineTests
{
    // ---------------------------------------------------------------
    // Constants
    // ---------------------------------------------------------------

    [Fact]
    public void Constants_AreReasonable()
    {
        Assert.InRange(IndexSearchEngine.VectorWeight, 0, 1);
        Assert.InRange(IndexSearchEngine.KeywordWeight, 0, 1);
        Assert.Equal(1.0, IndexSearchEngine.VectorWeight + IndexSearchEngine.KeywordWeight, 6);
        Assert.True(IndexSearchEngine.MaxLimit > 0);
        Assert.Equal(100, IndexSearchEngine.MaxLimit);
        Assert.InRange(IndexSearchEngine.MinVectorScore, 0, 1);
        Assert.InRange(IndexSearchEngine.MinKeywordScore, 0, 1);
    }

    // ---------------------------------------------------------------
    // SearchResult record shape
    // ---------------------------------------------------------------

    [Fact]
    public void SearchResult_PropertiesAreAccessible()
    {
        var headingPath = new[] { "Section 1", "Subsection A" } as IReadOnlyList<string>;

        var result = new IndexSearchEngine.SearchResult(
            DocumentPath: "docs/report.docx",
            ChunkIndex: 0,
            Text: "Some chunk text content",
            HeadingPath: headingPath,
            Score: 0.85,
            VectorScore: 0.9,
            KeywordScore: 0.8
        );

        Assert.Equal("docs/report.docx", result.DocumentPath);
        Assert.Equal(0, result.ChunkIndex);
        Assert.Equal("Some chunk text content", result.Text);
        Assert.Equal(headingPath, result.HeadingPath);
        Assert.Equal(0.85, result.Score);
        Assert.Equal(0.9, result.VectorScore);
        Assert.Equal(0.8, result.KeywordScore);
    }

    [Fact]
    public void SearchResult_WithNullHeadingPath()
    {
        var result = new IndexSearchEngine.SearchResult(
            DocumentPath: "notes.txt",
            ChunkIndex: 1,
            Text: "Plain text chunk",
            HeadingPath: null,
            Score: 0.5,
            VectorScore: 0.5,
            KeywordScore: 0.0
        );

        Assert.Null(result.HeadingPath);
    }

    // ---------------------------------------------------------------
    // SearchResults record shape
    // ---------------------------------------------------------------

    [Fact]
    public void SearchResults_PropertiesAreAccessible()
    {
        var results = new List<IndexSearchEngine.SearchResult>
        {
            new("doc1.docx", 0, "text1", null, 0.9, 0.9, 0.5),
            new("doc2.docx", 1, "text2", null, 0.8, 0.8, 0.4),
        };

        var searchResults = new IndexSearchEngine.SearchResults(
            Query: "test query",
            TotalResults: 2,
            Limit: 10,
            Results: results
        );

        Assert.Equal("test query", searchResults.Query);
        Assert.Equal(2, searchResults.TotalResults);
        Assert.Equal(10, searchResults.Limit);
        Assert.Equal(2, searchResults.Results.Count);
    }

    [Fact]
    public void SearchResults_EmptyResults()
    {
        var searchResults = new IndexSearchEngine.SearchResults(
            Query: "nothing",
            TotalResults: 0,
            Limit: 10,
            Results: Array.Empty<IndexSearchEngine.SearchResult>()
        );

        Assert.Empty(searchResults.Results);
        Assert.Equal(0, searchResults.TotalResults);
    }

    // ---------------------------------------------------------------
    // Input validation
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_NullOrEmptyQuery_ThrowsArgumentException(string? invalidQuery)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            IndexSearchEngine.SearchAsync(
                invalidQuery!,
                "Host=localhost;Database=test",
                new FakeEmbeddingProvider()));

        Assert.Contains("query", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_NullConnectionString_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IndexSearchEngine.SearchAsync(
                "test query",
                null,
                new FakeEmbeddingProvider()));

        Assert.Contains("OFFICE_DB_CONNECTION", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_EmptyConnectionString_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IndexSearchEngine.SearchAsync(
                "test query",
                "",
                new FakeEmbeddingProvider()));

        Assert.Contains("OFFICE_DB_CONNECTION", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task SearchAsync_InvalidLimit_ThrowsArgumentOutOfRangeException(int invalidLimit)
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            IndexSearchEngine.SearchAsync(
                "test query",
                "Host=localhost;Database=test",
                new FakeEmbeddingProvider(),
                limit: invalidLimit));

        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingProviderFailure_ThrowsInvalidOperationException()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IndexSearchEngine.SearchAsync(
                "test query",
                "Host=localhost;Database=test",
                new FailingEmbeddingProvider()));

        Assert.Contains("Failed to embed", ex.Message);
    }

    // ---------------------------------------------------------------
    // Fake embedding providers for testing
    // ---------------------------------------------------------------

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
        {
            var results = new List<ReadOnlyMemory<float>>();
            foreach (var _ in texts)
            {
                // Return a 4-dimensional vector for testing
                results.Add(new ReadOnlyMemory<float>(new[] { 0.1f, 0.2f, 0.3f, 0.4f }));
            }
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
        }
    }

    private sealed class FailingEmbeddingProvider : IEmbeddingProvider
    {
        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
        {
            throw new HttpRequestException("API unavailable");
        }
    }
}