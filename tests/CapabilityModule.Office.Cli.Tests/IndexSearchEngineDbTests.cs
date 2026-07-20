using Npgsql;

namespace CapabilityModule.Office.Cli.Tests;

/// <summary>
/// Integration tests for <see cref="IndexSearchEngine"/> against a real Postgres +
/// pgvector database. <see cref="IndexSearchEngineTests"/> (unit-level, no DB)
/// covers input validation and record shapes; this class exercises the actual
/// hybrid SQL — vector ranking, keyword-only matches, and the subdirectory
/// path-boundary fix that had no coverage before this file.
/// </summary>
[Collection("Postgres")]
public sealed class IndexSearchEngineDbTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private const int Dim = OpenAIEmbeddingProvider.Dimension;

    public IndexSearchEngineDbTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE documents, chunks RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_RanksClosestVectorFirst()
    {
        var docId = await SeedDocumentAsync("vectors.txt");
        await SeedChunkAsync(docId, 0, "chunk aligned with the query vector", BasisVector(0));
        await SeedChunkAsync(docId, 1, "chunk partially aligned with the query vector", MixedVector(0, 1));

        var provider = new FixedVectorEmbeddingProvider(BasisVector(0)); // identical to chunk 0

        // A query phrase absent from both chunks, so ranking here is driven
        // purely by vector similarity, not keyword overlap.
        var results = await IndexSearchEngine.SearchAsync(
            "zzz nonexistent keyword", _postgres.ConnectionString, provider);

        Assert.Equal(2, results.Results.Count);
        Assert.Equal("chunk aligned with the query vector", results.Results[0].Text);
        Assert.Equal("chunk partially aligned with the query vector", results.Results[1].Text);
        Assert.True(results.Results[0].VectorScore > results.Results[1].VectorScore);
    }

    [Fact]
    public async Task SearchAsync_KeywordOnlyMatch_StillSurfacesDespiteZeroVectorScore()
    {
        var docId = await SeedDocumentAsync("keywords.txt");
        await SeedChunkAsync(docId, 0, "the quarterly compliance report is attached", BasisVector(5));

        // Orthogonal to the chunk's vector — vector_score will be exactly 0 —
        // but the chunk still surfaces because the query text matches on keywords.
        var provider = new FixedVectorEmbeddingProvider(BasisVector(999));

        var results = await IndexSearchEngine.SearchAsync(
            "compliance report", _postgres.ConnectionString, provider);

        Assert.Single(results.Results);
        Assert.True(results.Results[0].KeywordScore > 0);
        Assert.Equal(0, results.Results[0].VectorScore, 3);
    }

    [Fact]
    public async Task SearchAsync_SubdirectoryFilter_AnchorsOnPathBoundary()
    {
        // Regression test for the LIKE-prefix boundary fix: scoping to "reports"
        // must not also match "reports-legacy/...".
        var reportsDoc = await SeedDocumentAsync("reports/current.txt");
        var legacyDoc = await SeedDocumentAsync("reports-legacy/old.txt");

        var vector = BasisVector(10);
        await SeedChunkAsync(reportsDoc, 0, "budget forecast for this year", vector);
        await SeedChunkAsync(legacyDoc, 0, "budget forecast from an old report", vector);

        var provider = new FixedVectorEmbeddingProvider(vector);

        var results = await IndexSearchEngine.SearchAsync(
            "budget forecast", _postgres.ConnectionString, provider, subdirectoryFilter: "reports");

        Assert.Single(results.Results);
        Assert.Equal("reports/current.txt", results.Results[0].DocumentPath);
    }

    [Fact]
    public async Task SearchAsync_NoSubdirectoryFilter_MatchesBothDirectories()
    {
        var reportsDoc = await SeedDocumentAsync("reports/current.txt");
        var legacyDoc = await SeedDocumentAsync("reports-legacy/old.txt");

        var vector = BasisVector(10);
        await SeedChunkAsync(reportsDoc, 0, "budget forecast for this year", vector);
        await SeedChunkAsync(legacyDoc, 0, "budget forecast from an old report", vector);

        var provider = new FixedVectorEmbeddingProvider(vector);

        var results = await IndexSearchEngine.SearchAsync(
            "budget forecast", _postgres.ConnectionString, provider);

        Assert.Equal(2, results.Results.Count);
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        var docId = await SeedDocumentAsync("many-chunks.txt");
        for (var i = 0; i < 5; i++)
        {
            await SeedChunkAsync(docId, i, $"chunk number {i} about widgets", BasisVector(i));
        }

        var provider = new FixedVectorEmbeddingProvider(BasisVector(0));

        var results = await IndexSearchEngine.SearchAsync(
            "widgets", _postgres.ConnectionString, provider, limit: 2);

        Assert.Equal(2, results.Results.Count);
        Assert.Equal(2, results.Limit);
    }

    // ---------------------------------------------------------------
    // Seeding helpers
    // ---------------------------------------------------------------

    private static float[] BasisVector(int index)
    {
        var v = new float[Dim];
        v[index] = 1.0f;
        return v;
    }

    private static float[] MixedVector(params int[] indices)
    {
        var v = new float[Dim];
        foreach (var i in indices) v[i] = 1.0f;
        return v;
    }

    private async Task<long> SeedDocumentAsync(string relativePath, string sourceFormat = "txt")
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (relative_path, content_hash, source_format)
            VALUES ($1, $2, $3) RETURNING id
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = relativePath });
        cmd.Parameters.Add(new NpgsqlParameter { Value = "hash-" + relativePath });
        cmd.Parameters.Add(new NpgsqlParameter { Value = sourceFormat });
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task SeedChunkAsync(long documentId, int chunkIndex, string text, float[] vector)
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        var vectorLiteral = "[" + string.Join(",", vector) + "]";

        cmd.CommandText = """
            INSERT INTO chunks (document_id, chunk_index, chunk_text, vector)
            VALUES ($1, $2, $3, $4::vector)
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = documentId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunkIndex });
        cmd.Parameters.Add(new NpgsqlParameter { Value = text });
        cmd.Parameters.Add(new NpgsqlParameter { Value = vectorLiteral });
        await cmd.ExecuteNonQueryAsync();
    }

    private sealed class FixedVectorEmbeddingProvider : IEmbeddingProvider
    {
        private readonly float[] _vector;
        public FixedVectorEmbeddingProvider(float[] vector) => _vector = vector;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
        {
            var results = texts.Select(_ => new ReadOnlyMemory<float>(_vector)).ToList();
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
        }
    }
}
