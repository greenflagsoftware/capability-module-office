using System.Text.Json;
using Npgsql;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Coordinates the indexing pipeline: walks a directory, extracts content,
/// chunks it, writes results to Postgres, and optionally generates vector
/// embeddings. Idempotent per file via SHA-256 content hash.
/// </summary>
internal static class IndexEngine
{
    /// <summary>
    /// Runs the index-build pipeline for all supported files under the given
    /// directory. Returns a summary of what was done.
    ///
    /// Per-file errors are written to stderr as they occur, consistent with
    /// the CLI error contract (errors surface via stderr/exit code, not the
    /// JSON payload).
    /// </summary>
    public static async Task<IndexSummary> BuildIndexAsync(
        string root,
        string directory,
        string? connectionString,
        IEmbeddingProvider? embeddingProvider = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "OFFICE_DB_CONNECTION environment variable is not set. " +
                "Indexing requires a Postgres database.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);

        var summary = new IndexSummary();

        foreach (var filePath in Directory.EnumerateFiles(
            directory, "*", SearchOption.AllDirectories))
        {
            var result = await IndexFileAsync(root, filePath, dataSource, embeddingProvider);
            if (result.Status == IndexStatus.Error)
            {
                Console.Error.WriteLine(
                    $"error: [{result.RelativePath}] {result.ErrorMessage}");
            }
            summary.Accumulate(result);
        }

        // If embedding is enabled, also embed chunks that were left without vectors
        // (e.g. from a prior index build that didn't embed)
        if (embeddingProvider is not null)
        {
            var existingCount = await EmbedMissingChunksAsync(dataSource, embeddingProvider);
            summary.ExistingChunksEmbedded = existingCount;
        }

        return summary;
    }

    /// <summary>
    /// Indexes a single file: checks content hash, extracts, chunks, writes,
    /// and optionally embeds.
    /// </summary>
    internal static async Task<FileIndexResult> IndexFileAsync(
        string root,
        string filePath,
        NpgsqlDataSource dataSource,
        IEmbeddingProvider? embeddingProvider = null)
    {
        try
        {
            IContentExtractor extractor;
            try
            {
                extractor = ContentExtractorFactory.GetExtractor(filePath);
            }
            catch (InvalidDataException)
            {
                return new FileIndexResult
                {
                    RelativePath = Path.GetRelativePath(root, filePath),
                    Status = IndexStatus.Skipped,
                };
            }

            var relativePath = Path.GetRelativePath(root, filePath);
            var contentHash = ChunkingEngine.ComputeFileHash(filePath);
            var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

            var existingHash = await GetDocumentHashAsync(dataSource, relativePath);
            if (existingHash == contentHash)
            {
                return new FileIndexResult
                {
                    RelativePath = relativePath,
                    Status = IndexStatus.Unchanged,
                };
            }

            var document = extractor.Extract(filePath);
            var chunks = ChunkingEngine.ChunkDocument(document);

            if (existingHash is not null)
            {
                await DeleteDocumentAsync(dataSource, relativePath);
            }

            var docId = await InsertDocumentAsync(
                dataSource, relativePath, contentHash, extension);

            var chunkIds = new List<long>();
            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                var chunkId = await InsertChunkAsync(
                    dataSource, docId, index, chunk);
                chunkIds.Add(chunkId);
            }

            // Embed the chunks if a provider is configured
            var chunksEmbedded = 0;
            if (embeddingProvider is not null && chunks.Count > 0)
            {
                try
                {
                    var texts = chunks.Select(c => c.Text).ToList();
                    var vectors = await embeddingProvider.EmbedAsync(texts);

                    for (var i = 0; i < chunkIds.Count && i < vectors.Count; i++)
                    {
                        await UpdateChunkVectorAsync(dataSource, chunkIds[i], vectors[i]);
                    }
                    chunksEmbedded = vectors.Count;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"error: [{relativePath}] embedding failed: {ex.Message}");
                }
            }

            return new FileIndexResult
            {
                RelativePath = relativePath,
                Status = IndexStatus.Indexed,
                ChunksWritten = chunks.Count,
                ChunksEmbedded = chunksEmbedded,
            };
        }
        catch (Exception ex)
        {
            return new FileIndexResult
            {
                RelativePath = Path.GetRelativePath(root, filePath),
                Status = IndexStatus.Error,
                ErrorMessage = ex.Message,
            };
        }
    }

    /// <summary>
    /// Finds chunks with NULL vector and embeds them. Returns the count of
    /// chunks that were embedded.
    /// </summary>
    private static async Task<int> EmbedMissingChunksAsync(
        NpgsqlDataSource dataSource, IEmbeddingProvider embeddingProvider)
    {
        var embedded = 0;
        const int batchSize = 100;

        while (true)
        {
            // Fetch a batch of chunk IDs and texts where vector IS NULL
            List<(long id, string text)> pending;
            await using (var conn = await dataSource.OpenConnectionAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT id, chunk_text FROM chunks
                    WHERE vector IS NULL
                    LIMIT $1
                    """;
                cmd.Parameters.Add(new NpgsqlParameter { Value = batchSize });

                pending = new List<(long, string)>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    pending.Add((reader.GetInt64(0), reader.GetString(1)));
                }
            }

            if (pending.Count == 0)
                break;

            // Embed this batch
            var texts = pending.Select(p => p.text).ToList();
            IReadOnlyList<ReadOnlyMemory<float>> vectors;
            try
            {
                vectors = await embeddingProvider.EmbedAsync(texts);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: embedding batch failed: {ex.Message}");
                break;
            }

            // Write vectors
            for (var i = 0; i < pending.Count && i < vectors.Count; i++)
            {
                await UpdateChunkVectorAsync(dataSource, pending[i].id, vectors[i]);
                embedded++;
            }
        }

        return embedded;
    }

    private static async Task<string?> GetDocumentHashAsync(
        NpgsqlDataSource dataSource, string relativePath)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM documents WHERE relative_path = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = relativePath });

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    private static async Task DeleteDocumentAsync(
        NpgsqlDataSource dataSource, string relativePath)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE relative_path = $1";
        cmd.Parameters.Add(new NpgsqlParameter { Value = relativePath });
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertDocumentAsync(
        NpgsqlDataSource dataSource, string relativePath,
        string contentHash, string sourceFormat)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (relative_path, content_hash, source_format)
            VALUES ($1, $2, $3)
            RETURNING id
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = relativePath });
        cmd.Parameters.Add(new NpgsqlParameter { Value = contentHash });
        cmd.Parameters.Add(new NpgsqlParameter { Value = sourceFormat });

        var result = await cmd.ExecuteScalarAsync();
        return (long)result!;
    }

    private static async Task<long> InsertChunkAsync(
        NpgsqlDataSource dataSource, long documentId,
        int chunkIndex, IndexChunk chunk)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        object? headingPathValue = chunk.HeadingPath.Count > 0
            ? JsonSerializer.Serialize(chunk.HeadingPath)
            : DBNull.Value;

        cmd.CommandText = """
            INSERT INTO chunks (document_id, chunk_index, chunk_text, heading_path)
            VALUES ($1, $2, $3, $4::jsonb)
            RETURNING id
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = documentId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunkIndex });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunk.Text });
        cmd.Parameters.Add(new NpgsqlParameter { Value = headingPathValue });

        var result = await cmd.ExecuteScalarAsync();
        return (long)result!;
    }

    private static async Task UpdateChunkVectorAsync(
        NpgsqlDataSource dataSource, long chunkId, ReadOnlyMemory<float> vector)
    {
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        // Build the pgvector literal: [1,2,3,...]
        var vectorArray = vector.Span.ToArray();
        var vectorLiteral = "[" + string.Join(",", vectorArray) + "]";

        cmd.CommandText = "UPDATE chunks SET vector = $1::vector WHERE id = $2";
        cmd.Parameters.Add(new NpgsqlParameter { Value = vectorLiteral });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunkId });

        await cmd.ExecuteNonQueryAsync();
    }
}

internal sealed class IndexSummary
{
    public int FilesProcessed { get; set; }
    public int FilesIndexed { get; set; }
    public int FilesUnchanged { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesWithErrors { get; set; }
    public int TotalChunksWritten { get; set; }
    public int TotalChunksEmbedded { get; set; }
    public int ExistingChunksEmbedded { get; set; }

    public void Accumulate(FileIndexResult result)
    {
        FilesProcessed++;
        switch (result.Status)
        {
            case IndexStatus.Indexed:
                FilesIndexed++;
                TotalChunksWritten += result.ChunksWritten;
                TotalChunksEmbedded += result.ChunksEmbedded;
                break;
            case IndexStatus.Unchanged:
                FilesUnchanged++;
                break;
            case IndexStatus.Skipped:
                FilesSkipped++;
                break;
            case IndexStatus.Error:
                FilesWithErrors++;
                break;
        }
    }

    public Dictionary<string, object?> ToDictionary()
    {
        return new Dictionary<string, object?>
        {
            ["filesProcessed"] = FilesProcessed,
            ["filesIndexed"] = FilesIndexed,
            ["filesUnchanged"] = FilesUnchanged,
            ["filesSkipped"] = FilesSkipped,
            ["filesWithErrors"] = FilesWithErrors,
            ["totalChunksWritten"] = TotalChunksWritten,
            ["totalChunksEmbedded"] = TotalChunksEmbedded,
            ["existingChunksEmbedded"] = ExistingChunksEmbedded,
        };
    }
}

internal enum IndexStatus
{
    Indexed,
    Unchanged,
    Skipped,
    Error,
}

internal sealed class FileIndexResult
{
    public string RelativePath { get; init; } = "";
    public IndexStatus Status { get; init; }
    public int ChunksWritten { get; init; }
    public int ChunksEmbedded { get; init; }
    public string? ErrorMessage { get; init; }
}