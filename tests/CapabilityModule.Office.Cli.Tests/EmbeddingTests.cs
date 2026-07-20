using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace CapabilityModule.Office.Cli.Tests;

public class EmbeddingTests
{
    // ---------------------------------------------------------------
    // IEmbeddingProvider interface contract
    // ---------------------------------------------------------------

    [Fact]
    public void InterfaceExists_AndCanBeImplemented()
    {
        // Verify the interface is defined and has the expected method
        var type = typeof(IEmbeddingProvider);
        Assert.True(type.IsInterface);
        var method = type.GetMethod("EmbedAsync");
        Assert.NotNull(method);
        Assert.True(typeof(Task).IsAssignableFrom(method!.ReturnType));
    }

    // ---------------------------------------------------------------
    // OpenAIEmbeddingProvider tests (with mocked HTTP)
    // ---------------------------------------------------------------

    [Fact]
    public async Task OpenAIEmbeddingProvider_EmbedsSingleText()
    {
        var handler = new MockHttpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("text-embedding-3-small", body);
            Assert.Contains("Hello world", body);
            Assert.Equal("/v1/embeddings", request.RequestUri?.AbsolutePath);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"index":0,"embedding":[0.1,0.2,0.3]}]}"""),
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        using var provider = new OpenAIEmbeddingProvider(httpClient);

        var result = await provider.EmbedAsync(new[] { "Hello world" });

        Assert.Single(result);
        Assert.Equal(3, result[0].Span.Length);
        Assert.Equal(0.1f, result[0].Span[0]);
        Assert.Equal(0.2f, result[0].Span[1]);
        Assert.Equal(0.3f, result[0].Span[2]);
    }

    [Fact]
    public async Task OpenAIEmbeddingProvider_EmbedsMultipleTexts()
    {
        var handler = new MockHttpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("text-one", body);
            Assert.Contains("text-two", body);
            Assert.Equal("/v1/embeddings", request.RequestUri?.AbsolutePath);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"index":0,"embedding":[1.0]},{"index":1,"embedding":[2.0]}]}"""),
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        using var provider = new OpenAIEmbeddingProvider(httpClient);

        var result = await provider.EmbedAsync(new[] { "text-one", "text-two" });

        Assert.Equal(2, result.Count);
        Assert.Equal(1.0f, result[0].Span[0]);
        Assert.Equal(2.0f, result[1].Span[0]);
    }

    [Fact]
    public async Task OpenAIEmbeddingProvider_EmptyInput_ReturnsEmpty()
    {
        using var httpClient = new HttpClient(new MockHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            })))
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        using var provider = new OpenAIEmbeddingProvider(httpClient);

        var result = await provider.EmbedAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task OpenAIEmbeddingProvider_ApiError_Throws()
    {
        var handler = new MockHttpHandler(async _ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"invalid_api_key\"}"),
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        using var provider = new OpenAIEmbeddingProvider(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new[] { "test" }));
        Assert.Contains("Unauthorized", ex.Message);
        Assert.Contains("invalid_api_key", ex.Message);
    }

    [Fact]
    public async Task OpenAIEmbeddingProvider_WrongNumberOfEmbeddings_Throws()
    {
        var handler = new MockHttpHandler(async _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"index":0,"embedding":[1.0]},{"index":1,"embedding":[2.0]}]}"""),
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
        };
        using var provider = new OpenAIEmbeddingProvider(httpClient);

        // Request 3 texts but response only has 2 embeddings
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new[] { "a", "b", "c" }));
        Assert.Contains("unexpected number", ex.Message);
    }

    [Fact]
    public void OpenAIEmbeddingProvider_NoApiKey_Throws()
    {
        // Clear the env var and verify the constructor throws
        var existingKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var ex = Assert.Throws<InvalidOperationException>(
                () => new OpenAIEmbeddingProvider());
            Assert.Contains("OPENAI_API_KEY", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", existingKey);
        }
    }

    [Fact]
    public void OpenAIEmbeddingProvider_Constants_AreReasonable()
    {
        Assert.True(OpenAIEmbeddingProvider.MaxBatchSize > 0);
        Assert.Equal(1536, OpenAIEmbeddingProvider.Dimension);
    }

    // ---------------------------------------------------------------
    // OpenRouterEmbeddingProvider tests (with mocked HTTP)
    // ---------------------------------------------------------------

    [Fact]
    public async Task OpenRouterEmbeddingProvider_EmbedsSingleText()
    {
        var handler = new MockHttpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("openai/text-embedding-3-small", body);
            Assert.Contains("Hello world", body);
            Assert.Equal("/api/v1/embeddings", request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer test-key", request.Headers.Authorization?.ToString());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"index":0,"embedding":[0.1,0.2,0.3]}]}"""),
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };
        using var provider = new OpenRouterEmbeddingProvider(httpClient);

        var result = await provider.EmbedAsync(new[] { "Hello world" });

        Assert.Single(result);
        Assert.Equal(3, result[0].Span.Length);
        Assert.Equal(0.1f, result[0].Span[0]);
    }

    [Fact]
    public async Task OpenRouterEmbeddingProvider_UsesConfiguredModel()
    {
        var handler = new MockHttpHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("cohere/embed-english-v3.0", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":[{"index":0,"embedding":[1.0]}]}"""),
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };
        using var provider = new OpenRouterEmbeddingProvider(httpClient, model: "cohere/embed-english-v3.0");

        await provider.EmbedAsync(new[] { "test" });
    }

    [Fact]
    public async Task OpenRouterEmbeddingProvider_EmptyInput_ReturnsEmpty()
    {
        using var httpClient = new HttpClient(new MockHttpHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]")
            })))
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };
        using var provider = new OpenRouterEmbeddingProvider(httpClient);

        var result = await provider.EmbedAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task OpenRouterEmbeddingProvider_ApiError_Throws()
    {
        var handler = new MockHttpHandler(async _ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"error\":\"invalid_api_key\"}"),
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/"),
        };
        using var provider = new OpenRouterEmbeddingProvider(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new[] { "test" }));
        Assert.Contains("Unauthorized", ex.Message);
        Assert.Contains("invalid_api_key", ex.Message);
    }

    [Fact]
    public void OpenRouterEmbeddingProvider_NoApiKey_Throws()
    {
        var existingKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null);
            var ex = Assert.Throws<InvalidOperationException>(
                () => new OpenRouterEmbeddingProvider());
            Assert.Contains("OPENROUTER_API_KEY", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", existingKey);
        }
    }

    [Fact]
    public void OpenRouterEmbeddingProvider_DefaultModel_Is1536Dimensional()
    {
        // Documents the dimension the default model produces, matching the
        // vector(1536) column — not something the provider enforces at
        // runtime, but a schema-compatibility contract worth locking in.
        Assert.Equal(1536, OpenRouterEmbeddingProvider.DefaultDimension);
    }

    // ---------------------------------------------------------------
    // EmbeddingProviderFactory — config-driven provider selection
    // ---------------------------------------------------------------

    [Fact]
    public void Factory_NoEnvVarSet_DefaultsToOpenAI()
    {
        using var _ = WithEnv("EMBEDDING_PROVIDER", null);
        using var __ = WithEnv("OPENAI_API_KEY", "test-key");

        var provider = EmbeddingProviderFactory.Create();

        Assert.IsType<OpenAIEmbeddingProvider>(provider);
    }

    [Fact]
    public void Factory_ExplicitOpenAI_ReturnsOpenAIProvider()
    {
        using var _ = WithEnv("EMBEDDING_PROVIDER", "openai");
        using var __ = WithEnv("OPENAI_API_KEY", "test-key");

        var provider = EmbeddingProviderFactory.Create();

        Assert.IsType<OpenAIEmbeddingProvider>(provider);
    }

    [Fact]
    public void Factory_CaseInsensitive_ReturnsOpenAIProvider()
    {
        using var _ = WithEnv("EMBEDDING_PROVIDER", "OpenAI");
        using var __ = WithEnv("OPENAI_API_KEY", "test-key");

        var provider = EmbeddingProviderFactory.Create();

        Assert.IsType<OpenAIEmbeddingProvider>(provider);
    }

    [Fact]
    public void Factory_UnknownProvider_ThrowsWithSupportedListInMessage()
    {
        using var _ = WithEnv("EMBEDDING_PROVIDER", "voyage");

        var ex = Assert.Throws<InvalidOperationException>(() => EmbeddingProviderFactory.Create());

        Assert.Contains("voyage", ex.Message);
        Assert.Contains("openai", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_ExplicitOpenRouter_ReturnsOpenRouterProvider()
    {
        using var _ = WithEnv("EMBEDDING_PROVIDER", "openrouter");
        using var __ = WithEnv("OPENROUTER_API_KEY", "test-key");

        var provider = EmbeddingProviderFactory.Create();

        Assert.IsType<OpenRouterEmbeddingProvider>(provider);
    }

    /// <summary>
    /// Temporarily sets an environment variable, restoring its prior value on dispose.
    /// Keeps env-var-dependent tests from leaking state into other tests.
    /// </summary>
    private static IDisposable WithEnv(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new EnvRestorer(name, previous);
    }

    private sealed class EnvRestorer : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvRestorer(string name, string? previous)
        {
            _name = name;
            _previous = previous;
        }

        public void Dispose() => Environment.SetEnvironmentVariable(_name, _previous);
    }

    // ---------------------------------------------------------------
    // IndexEngine embedding integration (skip-already-embedded)
    // ---------------------------------------------------------------

    [Fact]
    public void IndexSummary_AccumulatesEmbeddingCounts()
    {
        var summary = new IndexSummary();

        summary.Accumulate(new FileIndexResult
        {
            RelativePath = "file1.docx",
            Status = IndexStatus.Indexed,
            ChunksWritten = 3,
            ChunksEmbedded = 3,
        });
        summary.Accumulate(new FileIndexResult
        {
            RelativePath = "file2.docx",
            Status = IndexStatus.Unchanged,
        });
        summary.Accumulate(new FileIndexResult
        {
            RelativePath = "file3.txt",
            Status = IndexStatus.Indexed,
            ChunksWritten = 2,
            ChunksEmbedded = 0, // embedding disabled for this file
        });

        Assert.Equal(3, summary.FilesProcessed);
        Assert.Equal(2, summary.FilesIndexed);
        Assert.Equal(1, summary.FilesUnchanged);
        Assert.Equal(5, summary.TotalChunksWritten);
        Assert.Equal(3, summary.TotalChunksEmbedded);
    }

    [Fact]
    public void IndexSummary_ToDictionary_IncludesEmbeddingFields()
    {
        var summary = new IndexSummary
        {
            FilesProcessed = 5,
            FilesIndexed = 3,
            TotalChunksWritten = 10,
            TotalChunksEmbedded = 8,
            ExistingChunksEmbedded = 2,
        };

        var dict = summary.ToDictionary();

        Assert.Equal(5, dict["filesProcessed"]);
        Assert.Equal(10, dict["totalChunksWritten"]);
        Assert.Equal(8, dict["totalChunksEmbedded"]);
        Assert.Equal(2, dict["existingChunksEmbedded"]);
    }

    // ---------------------------------------------------------------
    // Mock HTTP handler for testing
    // ---------------------------------------------------------------

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _handler(request);
        }
    }
}