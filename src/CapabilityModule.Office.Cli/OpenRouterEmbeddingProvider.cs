using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Embedding provider that routes through OpenRouter's OpenAI-compatible
/// <c>/embeddings</c> endpoint instead of calling OpenAI directly — useful
/// when the caller already has an OpenRouter key/credits but no direct
/// OpenAI API key. The API key is sourced from the
/// <c>OPENROUTER_API_KEY</c> environment variable — never logged.
///
/// Defaults to routing to <c>openai/text-embedding-3-small</c> so the
/// output stays 1536-dimensional, matching the <c>vector(1536)</c> column
/// this module's schema already has (see db/migrations). Overriding
/// <c>OPENROUTER_EMBEDDING_MODEL</c> to a model with different
/// dimensionality requires a schema change and a full re-index — same
/// caveat <see cref="IEmbeddingProvider"/> already calls out for switching
/// providers in general.
/// </summary>
internal sealed class OpenRouterEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    /// <summary>
    /// Conservative batch cap — OpenRouter proxies to varying upstream
    /// providers/models with their own limits, so this stays well under
    /// OpenAI's own 2048-per-request limit rather than assuming parity.
    /// </summary>
    internal const int MaxBatchSize = 512;

    /// <summary>
    /// The embedding dimension for the default model
    /// (openai/text-embedding-3-small, routed via OpenRouter).
    /// </summary>
    internal const int DefaultDimension = 1536;

    public OpenRouterEmbeddingProvider(string? apiKey = null, string? model = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENROUTER_API_KEY is not set. Set it as an environment variable or pass it explicitly.");

        _model = model
            ?? Environment.GetEnvironmentVariable("OPENROUTER_EMBEDDING_MODEL")
            ?? "openai/text-embedding-3-small";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
            Timeout = TimeSpan.FromSeconds(120),
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// Override HttpClient for testing. Not intended for production use.
    /// </summary>
    internal OpenRouterEmbeddingProvider(HttpClient httpClient, string? apiKey = null, string? model = null)
    {
        _apiKey = apiKey ?? "test-key";
        _model = model ?? "openai/text-embedding-3-small";
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
            return Array.Empty<ReadOnlyMemory<float>>();

        var results = new List<ReadOnlyMemory<float>>();

        for (var offset = 0; offset < texts.Count; offset += MaxBatchSize)
        {
            var batch = texts.Skip(offset).Take(MaxBatchSize).ToList();
            var vectors = await EmbedBatchAsync(batch);
            results.AddRange(vectors);
        }

        return results;
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(List<string> batch)
    {
        var request = new EmbeddingRequest
        {
            Model = _model,
            Input = batch,
        };

        var response = await _httpClient.PostAsJsonAsync("embeddings", request);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"OpenRouter embedding API returned {response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();

        if (result?.Data is null || result.Data.Count != batch.Count)
        {
            throw new InvalidOperationException(
                "OpenRouter embedding API returned an unexpected number of embeddings.");
        }

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => new ReadOnlyMemory<float>(d.Embedding))
            .ToList();
    }

    // JSON models for OpenRouter's OpenAI-compatible embeddings API

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = "";

        [JsonPropertyName("input")]
        public List<string> Input { get; init; } = new();
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; init; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; init; } = Array.Empty<float>();
    }
}
