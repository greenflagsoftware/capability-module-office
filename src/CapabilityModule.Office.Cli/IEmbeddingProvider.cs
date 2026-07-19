namespace CapabilityModule.Office.Cli;

/// <summary>
/// Provider-agnostic embedding interface. The concrete provider is selected
/// via configuration, not compile-time choice, so cost/vendor can change
/// without touching call sites.
///
/// Important: vector spaces aren't compatible across providers (different
/// dimensionality, different training). Switching providers always means
/// re-embedding every chunk from scratch — the interface saves a code
/// rewrite, not a re-index.
/// </summary>
internal interface IEmbeddingProvider
{
    /// <summary>
    /// Embeds a batch of text strings into vectors.
    /// </summary>
    /// <param name="texts">The texts to embed. Should be non-empty.</param>
    /// <returns>A list of vectors, one per input text, in the same order.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> texts);
}