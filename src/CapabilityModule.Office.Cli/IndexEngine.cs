using System.Text.Json;
using Npgsql;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Coordinates the indexing pipeline: walks a directory, extracts content,
/// chunks it, and writes results to Postgres. Idempotent per file via SHA-256
/// content hash.
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
        string? connectionString)
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
            var result = await IndexFileAsync(root, filePath, dataSource);
            if (result.Status == IndexStatus.Error)
            {
                Console.Error.WriteLine(
                    $"error: [{result.RelativePath}] {result.ErrorMessage}");
            }
            summary.Accumulate(result);
        }

        return summary;
    }

    /// <summary>
    /// Indexes a single file: checks content hash, extracts, chunks, and writes.
    /// </summary>
    internal static async Task<FileIndexResult> IndexFileAsync(
        string root,
        string filePath,
        NpgsqlDataSource dataSource)
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
                // Unsupported format — skip silently (only warn if it's a common
                // binary that might be expected; for now just skip).
                return new FileIndexResult
                {
                    RelativePath = Path.GetRelativePath(root, filePath),
                    Status = IndexStatus.Skipped,
                };
            }

            var relativePath = Path.GetRelativePath(root, filePath);
            var contentHash = ChunkingEngine.ComputeFileHash(filePath);
            var extension = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

            // Check if the file is already indexed with the same hash
            var existingHash = await GetDocumentHashAsync(dataSource, relativePath);
            if (existingHash == contentHash)
            {
                return new FileIndexResult
                {
                    RelativePath = relativePath,
                    Status = IndexStatus.Unchanged,
                };
            }

            // Extract content
            var document = extractor.Extract(filePath);

            // Chunk
            var chunks = ChunkingEngine.ChunkDocument(document);

            // Delete prior chunks if this file was already indexed
            if (existingHash is not null)
            {
                await DeleteDocumentAsync(dataSource, relativePath);
            }

            // Write document + chunks
            var docId = await InsertDocumentAsync(
                dataSource, relativePath, contentHash, extension);

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                await InsertChunkAsync(
                    dataSource, docId, index, chunk);
            }

            return new FileIndexResult
            {
                RelativePath = relativePath,
                Status = IndexStatus.Indexed,
                ChunksWritten = chunks.Count,
            };
        }
        catch (Exception ex)
        {
            // Errors surface via exit code/stderr per the CLI contract
            return new FileIndexResult
            {
                RelativePath = Path.GetRelativePath(root, filePath),
                Status = IndexStatus.Error,
                ErrorMessage = ex.Message,
            };
        }
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

    private static async Task InsertChunkAsync(
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
            """;
        cmd.Parameters.Add(new NpgsqlParameter { Value = documentId });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunkIndex });
        cmd.Parameters.Add(new NpgsqlParameter { Value = chunk.Text });
        cmd.Parameters.Add(new NpgsqlParameter { Value = headingPathValue });

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

    public void Accumulate(FileIndexResult result)
    {
        FilesProcessed++;
        switch (result.Status)
        {
            case IndexStatus.Indexed:
                FilesIndexed++;
                TotalChunksWritten += result.ChunksWritten;
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
    public string? ErrorMessage { get; init; }
}