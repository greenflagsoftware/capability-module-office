using Npgsql;

namespace CapabilityModule.Office.Cli.Tests;

/// <summary>
/// Integration tests for <see cref="IndexEngine"/> against a real Postgres +
/// pgvector database (see <see cref="PostgresFixture"/>). These close the gap
/// flagged in the Phase 7-11 architecture review: the idempotent-reindex and
/// skip-already-embedded behavior were previously only exercised manually via
/// docker-compose, never by an automated test.
/// </summary>
[Collection("Postgres")]
public sealed class IndexEngineTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private readonly string _dir;

    public IndexEngineTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _dir = Path.Combine(Path.GetTempPath(), "office-cli-index-engine-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public async Task InitializeAsync()
    {
        // Reset table state before every test — xunit constructs a fresh
        // instance of this class per test method, so this runs per test.
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "TRUNCATE documents, chunks RESTART IDENTITY CASCADE";
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    // ---------------------------------------------------------------
    // Basic indexing
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildIndexAsync_IndexesDocxAndTxtFiles()
    {
        DocxEngine.Create(PathFor("report.docx"), "Report", "First paragraph.\nSecond paragraph.");
        File.WriteAllText(PathFor("notes.txt"), "Plain text notes.");

        var summary = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        Assert.Equal(2, summary.FilesProcessed);
        Assert.Equal(2, summary.FilesIndexed);
        Assert.Equal(0, summary.FilesWithErrors);
        Assert.True(summary.TotalChunksWritten > 0);

        var (docCount, chunkCount) = await CountRowsAsync();
        Assert.Equal(2, docCount);
        Assert.Equal(summary.TotalChunksWritten, chunkCount);
    }

    [Fact]
    public async Task BuildIndexAsync_WritesRelativePathAndChunkText()
    {
        File.WriteAllText(PathFor("hello.txt"), "Hello from the index engine test.");

        await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT d.relative_path, c.chunk_text FROM documents d JOIN chunks c ON c.document_id = d.id";
        await using var reader = await cmd.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("hello.txt", reader.GetString(0));
        Assert.Contains("Hello from the index engine test.", reader.GetString(1));
    }

    // ---------------------------------------------------------------
    // Idempotent re-index (Phase 9 exit criterion)
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildIndexAsync_RerunWithNoChanges_ReportsUnchanged_NoDuplicateChunks()
    {
        File.WriteAllText(PathFor("stable.txt"), "This file never changes between runs.");

        var first = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);
        Assert.Equal(1, first.FilesIndexed);
        var (_, chunksAfterFirst) = await CountRowsAsync();

        var second = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        Assert.Equal(0, second.FilesIndexed);
        Assert.Equal(1, second.FilesUnchanged);
        var (docsAfterSecond, chunksAfterSecond) = await CountRowsAsync();
        Assert.Equal(1, docsAfterSecond);
        Assert.Equal(chunksAfterFirst, chunksAfterSecond); // no duplicate chunks written
    }

    [Fact]
    public async Task BuildIndexAsync_ChangedFileContent_ReplacesOldChunks()
    {
        var file = PathFor("changing.txt");
        File.WriteAllText(file, "Original content about apples.");
        await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        File.WriteAllText(file, "Completely different content about oranges and grapefruit.");
        var second = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        Assert.Equal(1, second.FilesIndexed);
        Assert.Equal(0, second.FilesUnchanged);

        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();

        // Exactly one document row (the old one was deleted and replaced, not duplicated)
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM documents";
            var docCount = (long)(await countCmd.ExecuteScalarAsync())!;
            Assert.Equal(1L, docCount);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT chunk_text FROM chunks";
        await using var reader = await cmd.ExecuteReaderAsync();
        var allText = "";
        while (await reader.ReadAsync())
            allText += reader.GetString(0);

        Assert.DoesNotContain("apples", allText);
        Assert.Contains("oranges", allText);
    }

    // ---------------------------------------------------------------
    // Unsupported format handling
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildIndexAsync_UnsupportedFormatFile_IsSkippedNotError()
    {
        File.WriteAllText(PathFor("readme"), "no extension, no adapter");
        File.WriteAllText(PathFor("data.xyz"), "unknown extension");
        File.WriteAllText(PathFor("valid.txt"), "this one has a supported adapter");

        var summary = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);

        Assert.Equal(2, summary.FilesSkipped);
        Assert.Equal(0, summary.FilesWithErrors);
        Assert.Equal(1, summary.FilesIndexed);

        var (docCount, _) = await CountRowsAsync();
        Assert.Equal(1, docCount); // only the supported file was written
    }

    // ---------------------------------------------------------------
    // Embedding: skip-already-embedded (Phase 10 exit criterion)
    // ---------------------------------------------------------------

    [Fact]
    public async Task BuildIndexAsync_WithEmbeddingProvider_PopulatesVectorColumn()
    {
        File.WriteAllText(PathFor("embed-me.txt"), "Content that should get an embedding.");
        var provider = new FakeEmbeddingProvider();

        var summary = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString, provider);

        Assert.True(summary.TotalChunksEmbedded > 0);
        Assert.Equal(0, await CountNullVectorsAsync());
    }

    [Fact]
    public async Task BuildIndexAsync_ExistingUnembeddedChunks_AreBackfilled_ThenNotReEmbedded()
    {
        // First pass: index without an embedding provider — chunks land with NULL vectors.
        File.WriteAllText(PathFor("backfill.txt"), "This chunk starts life with no vector.");
        var firstPass = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString);
        Assert.Equal(0, firstPass.TotalChunksEmbedded);
        Assert.True(await CountNullVectorsAsync() > 0);

        // Second pass: same unchanged file, but now with a provider — the file itself
        // is "Unchanged" (skips per-file embedding), so any embedding here must come
        // from the existing-chunk backfill path.
        var provider = new FakeEmbeddingProvider();
        var secondPass = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString, provider);

        Assert.Equal(0, secondPass.FilesIndexed); // file unchanged
        Assert.True(secondPass.ExistingChunksEmbedded > 0);
        Assert.Equal(0, await CountNullVectorsAsync());
        var callsAfterBackfill = provider.CallCount;

        // Third pass: everything already embedded — no further provider calls,
        // no further backfill work. This is the "skip-already-embedded" behavior.
        var thirdPass = await IndexEngine.BuildIndexAsync(_dir, _dir, _postgres.ConnectionString, provider);

        Assert.Equal(0, thirdPass.ExistingChunksEmbedded);
        Assert.Equal(callsAfterBackfill, provider.CallCount); // no new embedding calls made
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private async Task<(long documents, long chunks)> CountRowsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();

        await using var docCmd = conn.CreateCommand();
        docCmd.CommandText = "SELECT COUNT(*) FROM documents";
        var docCount = (long)(await docCmd.ExecuteScalarAsync())!;

        await using var chunkCmd = conn.CreateCommand();
        chunkCmd.CommandText = "SELECT COUNT(*) FROM chunks";
        var chunkCount = (long)(await chunkCmd.ExecuteScalarAsync())!;

        return (docCount, chunkCount);
    }

    private async Task<long> CountNullVectorsAsync()
    {
        await using var dataSource = NpgsqlDataSource.Create(_postgres.ConnectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chunks WHERE vector IS NULL";
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Produces deterministic, schema-valid (1536-dimension) embeddings without
    /// calling any real API. Tracks call count so tests can assert that already-
    /// embedded chunks don't trigger further provider calls.
    /// </summary>
    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
        {
            CallCount++;
            var results = new List<ReadOnlyMemory<float>>();
            foreach (var text in texts)
            {
                var vector = new float[OpenAIEmbeddingProvider.Dimension];
                vector[Math.Abs(text.GetHashCode()) % vector.Length] = 1.0f;
                results.Add(new ReadOnlyMemory<float>(vector));
            }
            return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(results);
        }
    }
}
