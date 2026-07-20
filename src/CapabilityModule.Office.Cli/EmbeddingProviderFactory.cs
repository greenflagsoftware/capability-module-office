namespace CapabilityModule.Office.Cli;

/// <summary>
/// Selects the configured <see cref="IEmbeddingProvider"/> implementation.
/// This is the piece that actually delivers on <see cref="IEmbeddingProvider"/>'s
/// stated purpose — swapping vendors via config, not a call-site edit. Selection
/// is read from the EMBEDDING_PROVIDER environment variable (default "openai").
/// </summary>
internal static class EmbeddingProviderFactory
{
    internal const string DefaultProvider = "openai";

    /// <summary>
    /// Creates the configured embedding provider.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if EMBEDDING_PROVIDER names a provider with no implementation
    /// registered here, or if the selected provider's own required config
    /// (e.g. an API key) is missing.
    /// </exception>
    public static IEmbeddingProvider Create()
    {
        var configured = Environment.GetEnvironmentVariable("EMBEDDING_PROVIDER");
        var providerName = string.IsNullOrWhiteSpace(configured)
            ? DefaultProvider
            : configured.Trim().ToLowerInvariant();

        return providerName switch
        {
            "openai" => new OpenAIEmbeddingProvider(),
            _ => throw new InvalidOperationException(
                $"Unknown EMBEDDING_PROVIDER '{providerName}'. Supported providers: {DefaultProvider}. " +
                "Unset EMBEDDING_PROVIDER to use the default, or add an implementation for this value.")
        };
    }
}
