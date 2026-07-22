using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Hybrid search engine that queries the indexed document store using both
/// vector similarity (pgvector cosine distance) and keyword relevance
/// (Postgres tsvector/ts_rank). Results are ranked by a weighted combination
/// of both signals.
///
/// Requires a populated index (Phase 9) and embedded chunks (Phase 10).
/// Chunks without vectors contribute only keyword score; chunks without
/// tsvector data contribute only vector score; chunks with both contribute
/// the full hybrid score.
/// </summary>
internal static class IndexSearchEngine
{
    /// <summary>
    /// Weight applied to the vector similarity score in the combined ranking.
    /// </summary>
    internal const double VectorWeight = 0.5;

    /// <summary>
    /// Weight applied to the keyword relevance score in the combined ranking.
    /// </summary>
    internal const double KeywordWeight = 0.5;

    /// <summary>
    /// Maximum number of results returned by a single search query.
    /// </summary>
    internal const int MaxLimit = 100;

    /// <summary>
    /// Minimum cosine-similarity vector score for a chunk to be considered a match
    /// on the vector signal alone. Cosine similarity is a continuous value that is
    /// almost never exactly 0, so filtering on "> 0" (the previous behavior) let
    /// essentially every indexed chunk through regardless of relevance. Tune based
    /// on real query traffic (see docs/DEV_PLAN.md Phase 11 note).
    /// </summary>
    internal const double MinVectorScore = 0.35;

    /// <summary>
    /// Minimum ts_rank keyword score for a chunk to be considered a match on the
    /// keyword signal alone. Kept low relative to <see cref="MinVectorScore"/>
    /// because ts_rank and cosine similarity live on different scales — a single
    /// exact-phrase keyword match on a short chunk commonly scores well under 0.1.
    /// </summary>
    internal const double MinKeywordScore = 0.02;

    /// <summary>
    /// A single search result hit.
    /// </summary>
    /// <param name="DocumentPath">Restricted-root-relative path to the source document.</param>
    /// <param name="ChunkIndex">0-based index of this chunk within the document.</param>
    /// <param name="Text">The chunk text content.</param>
    /// <param name="HeadingPath">Structural heading path, if available (e.g. ["Section 1", "Subsection A"]).</param>
    /// <param name="Score">Combined relevance score (higher = more relevant).</param>
    /// <param name="VectorScore">Cosine-similarity contribution (0–1 range).</param>
    /// <param name="KeywordScore">ts_rank keyword relevance contribution.</param>
    public sealed record SearchResult(
        string DocumentPath,
        int ChunkIndex,
        string Text,
        IReadOnlyList<string>? HeadingPath,
        double Score,
        double VectorScore,
        double KeywordScore
    );

    /// <summary>
    /// The complete set of search results for a query.
    /// </summary>
    /// <param name="Query">The original query text.</param>
    /// <param name="TotalResults">Number of results returned (capped by <paramref name="Limit"/>).</param>
    /// <param name="Limit">The maximum number of results requested.</param>
    /// <param name="Results">The ranked result list.</param>
    public sealed record SearchResults(
        string Query,
        int TotalResults,
        int Limit,
        IReadOnlyList<SearchResult> Results
    );

    /// <summary>
    /// Runs a hybrid search over the indexed document store.
    /// </summary>
    /// <param name="query">Free-text search query.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="embeddingProvider">Provider used to embed the query text for vector search.</param>
    /// <param name="subdirectoryFilter">Optional restricted-root-relative subdirectory to scope the search (e.g. "reports/legal").</param>
    /// <param name="limit">Maximum number of results to return (default 10, capped at <see cref="MaxLimit"/>).</param>
    /// <returns>A <see cref="SearchResults"/> with ranked hits.</returns>
    public static async Task<SearchResults> SearchAsync(
        string query,
        string? connectionString,
        IEmbeddingProvider embeddingProvider,
        string? subdirectoryFilter = null,
        int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query must not be null or empty.", nameof(query));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "OFFICE_DB_CONNECTION environment variable is not set. " +
                "Searching requires a Postgres database.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        limit = Math.Min(limit, MaxLimit);

        // Embed the query text for vector similarity search
        IReadOnlyList<ReadOnlyMemory<float>> queryVectors;
        try
        {
            queryVectors = await embeddingProvider.EmbedAsync(new[] { query });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to embed search query: {ex.Message}", ex);
        }

        if (queryVectors.Count == 0)
        {
            throw new InvalidOperationException(
                "Embedding provider returned zero vectors for the search query.");
        }

        var queryVector = queryVectors[0];

        // Anchor the subdirectory filter on a path-segment boundary (trailing
        // slash) so scoping to "reports" doesn't also match "reports-legacy/...".
        var pathPrefix = subdirectoryFilter is null
            ? null
            : subdirectoryFilter.TrimEnd('/', '\\') + "/";

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var conn = await dataSource.OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();

        // Build the query vector literal for pgvector: [0.1,0.2,...]
        var vectorSpan = queryVector.Span;
        var vectorLiteral = "[" + string.Join(",", vectorSpan.ToArray()) + "]";

        // Hybrid SQL: combine vector similarity (cosine) with keyword relevance (ts_rank)
        // using a weighted sum. NULL-safe: chunks missing one signal still contribute the other.
        var sql = """
            SELECT
                d.relative_path,
                c.chunk_index,
                c.chunk_text,
                c.heading_path,
                COALESCE(1 - (c.vector <=> $1::vector), 0) AS vector_score,
                COALESCE(ts_rank(c.search_vector, plainto_tsquery('english', $2)), 0) AS keyword_score,
                (
                    COALESCE(1 - (c.vector <=> $1::vector), 0) * $4 +
                    COALESCE(ts_rank(c.search_vector, plainto_tsquery('english', $2)), 0) * $5
                ) AS combined_score
            FROM chunks c
            JOIN documents d ON d.id = c.document_id
            WHERE ($3 IS NULL OR d.relative_path LIKE $3 || '%')
              -- Require a meaningfully relevant match on at least one signal.
              -- Thresholds differ per signal because ts_rank and cosine
              -- similarity live on different scales (see MinVectorScore /
              -- MinKeywordScore doc comments).
              AND (
                  COALESCE(1 - (c.vector <=> $1::vector), 0) >= $7
                  OR COALESCE(ts_rank(c.search_vector, plainto_tsquery('english', $2)), 0) >= $8
              )
            ORDER BY combined_score DESC
            LIMIT $6
            """;

        cmd.CommandText = sql;
        cmd.Parameters.Add(new NpgsqlParameter { Value = vectorLiteral });
        cmd.Parameters.Add(new NpgsqlParameter { Value = query });
        cmd.Parameters.Add(new NpgsqlParameter
        {
            // NpgsqlDbType must be set explicitly here: when pathPrefix is null
            // (the common case — no subdirectory scope), an untyped DBNull.Value
            // leaves Postgres unable to infer $3's type from this query shape,
            // and the query fails with "42P08: could not determine data type of
            // parameter $3" — a real bug this parameter had until an integration
            // test against a live database caught it.
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = (object?)pathPrefix ?? DBNull.Value
        });
        cmd.Parameters.Add(new NpgsqlParameter { Value = VectorWeight });
        cmd.Parameters.Add(new NpgsqlParameter { Value = KeywordWeight });
        cmd.Parameters.Add(new NpgsqlParameter { Value = limit });
        cmd.Parameters.Add(new NpgsqlParameter { Value = MinVectorScore });
        cmd.Parameters.Add(new NpgsqlParameter { Value = MinKeywordScore });

        var results = new List<SearchResult>();

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var relativePath = reader.GetString(0);
            var chunkIndex = reader.GetInt32(1);
            var chunkText = reader.GetString(2);
            var headingPathRaw = reader.IsDBNull(3) ? null : reader.GetString(3);
            var vectorScore = reader.GetDouble(4);
            var keywordScore = reader.GetDouble(5);
            var combinedScore = reader.GetDouble(6);

            IReadOnlyList<string>? headingPath = null;
            if (headingPathRaw is not null)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(headingPathRaw);
                    headingPath = parsed;
                }
                catch (JsonException)
                {
                    // Non-critical — heading path is auxiliary metadata
                }
            }

            results.Add(new SearchResult(
                relativePath,
                chunkIndex,
                chunkText,
                headingPath,
                combinedScore,
                vectorScore,
                keywordScore
            ));
        }

        return new SearchResults(query, results.Count, limit, results);
    }
}