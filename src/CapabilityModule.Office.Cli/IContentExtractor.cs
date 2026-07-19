namespace CapabilityModule.Office.Cli;

/// <summary>
/// Represents a chunk of extracted document content with structural context.
/// </summary>
public sealed class ContentChunk
{
    /// <summary>
    /// The extracted text of this chunk.
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// The heading path for this chunk, e.g. ["Section 1", "Subsection A"].
    /// Empty for documents with no recoverable heading structure.
    /// </summary>
    public IReadOnlyList<string> HeadingPath { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The 0-based page number for this chunk, if recoverable; -1 otherwise.
    /// </summary>
    public int PageNumber { get; init; } = -1;
}

/// <summary>
/// The normalized result of extracting content from a document.
/// The single normalized representation that chunking (Phase 9) operates against,
/// regardless of source format.
/// </summary>
public sealed class NormalizedDocument
{
    /// <summary>
    /// The plain text content of the document, with paragraph breaks preserved.
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// Paragraphs extracted from the document, preserving order.
    /// </summary>
    public IReadOnlyList<string> Paragraphs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The heading path for each paragraph, keyed by paragraph index (0-based).
    /// Only populated for formats that recover heading structure (e.g. .docx).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<string>> ParagraphHeadings { get; init; }
        = new Dictionary<int, IReadOnlyList<string>>();

    /// <summary>
    /// Chunks suitable for indexing. When structure is available (headings, pages),
    /// chunk boundaries follow that structure. Otherwise, chapters is a single
    /// entry containing the entire text (the caller splits further if needed).
    /// </summary>
    public IReadOnlyList<ContentChunk> Chapters { get; init; } = Array.Empty<ContentChunk>();
}

/// <summary>
/// Extracts normalized text and recoverable structure from a document file.
/// Implementations are selected by file extension via <see cref="ContentExtractorFactory"/>.
/// </summary>
internal interface IContentExtractor
{
    /// <summary>
    /// Extracts content from the given file and returns a normalized representation.
    /// </summary>
    /// <param name="filePath">The absolute, pre-validated path to the file.</param>
    /// <returns>A <see cref="NormalizedDocument"/> containing the extracted text and structure.</returns>
    NormalizedDocument Extract(string filePath);
}
