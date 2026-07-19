using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// OpenAI `text-embedding-3-small` provider. Batches texts and calls the
/// OpenAI embeddings API via HTTP. The API key is sourced from the
/// <c>OPENAI_API_KEY</c> environment variable — never logged.
/// </summary>
internal sealed class OpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    /// <summary>
    /// Maximum texts per API request (OpenAI limit for text-embedding-3-small).
    /// </summary>
    internal const int MaxBatchSize = 2048;

    /// <summary>
    /// The embedding dimension for text-embedding-3-small.
    /// </summary>
    internal const int Dimension = 1536;

    public OpenAIEmbeddingProvider(string? apiKey = null, string? model = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY is not set. Set it as an environment variable or pass it explicitly.");

        _model = model ?? "text-embedding-3-small";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
            Timeout = TimeSpan.FromSeconds(120),
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    /// <summary>
    /// Override HttpClient for testing. Not intended for production use.
    /// </summary>
    internal OpenAIEmbeddingProvider(HttpClient httpClient, string? apiKey = null, string? model = null)
    {
        _apiKey = apiKey ?? "test-key";
        _model = model ?? "text-embedding-3-small";
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
            return Array.Empty<ReadOnlyMemory<float>>();

        var results = new List<ReadOnlyMemory<float>>();

        // Process in batches
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
                $"OpenAI embedding API returned {response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();

        if (result?.Data is null || result.Data.Count != batch.Count)
        {
            throw new InvalidOperationException(
                "OpenAI embedding API returned an unexpected number of embeddings.");
        }

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => new ReadOnlyMemory<float>(d.Embedding))
            .ToList();
    }

    // JSON models for the OpenAI embeddings API

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