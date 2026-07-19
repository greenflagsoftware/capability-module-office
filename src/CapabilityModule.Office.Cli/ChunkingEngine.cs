using System.Security.Cryptography;
using System.Text;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Splits normalized document content into indexable chunks at ~200-500 token
/// granularity with ~10-20% overlap between adjacent chunks.
///
/// When structure is available (headings/pages from the extractor's Chapters),
/// chunk boundaries respect those structural boundaries first, then subdivide
/// oversized chapters further. For plain-text fallback (no structure), paragraphs
/// are used as the primary boundary unit.
/// </summary>
internal static class ChunkingEngine
{
    /// <summary>
    /// Target chunk size in tokens (approximate). Actual boundaries fall at
    /// structural or paragraph boundaries near this target.
    /// </summary>
    internal const int TargetTokensPerChunk = 400;

    /// <summary>
    /// Overlap in tokens between adjacent chunks.
    /// </summary>
    internal const int OverlapTokens = 60;

    /// <summary>
    /// Rough estimate: 1 token ≈ 4 characters for English text.
    /// </summary>
    private const double CharsPerToken = 4.0;

    internal static int TargetCharsPerChunk => (int)(TargetTokensPerChunk * CharsPerToken);
    internal static int OverlapChars => (int)(OverlapTokens * CharsPerToken);

    /// <summary>
    /// Computes the SHA-256 hash of a file's content as a lowercase hex string.
    /// </summary>
    public static string ComputeFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Chunks a normalized document into indexable pieces.
    ///
    /// If the extractor provided structural chapters, those are used as the
    /// starting point (each chapter may be further subdivided if oversized).
    /// Otherwise, paragraphs are chunked with the generic fallback.
    /// </summary>
    public static List<IndexChunk> ChunkDocument(NormalizedDocument document)
    {
        var result = new List<IndexChunk>();

        if (document.Chapters.Count > 0)
        {
            // Structure-aware: use chapters from the extractor
            foreach (var chapter in document.Chapters)
            {
                SubdivideContent(chapter.Text, chapter.HeadingPath, chapter.PageNumber, result);
            }
        }
        else if (document.Paragraphs.Count > 0)
        {
            // Paragraph-based fallback: group paragraphs into chunks near target size
            ChunkParagraphs(document.Paragraphs, result);
        }

        return result;
    }

    /// <summary>
    /// Subdivides text content into chunks at paragraph boundaries near the
    /// target token count, with overlap between adjacent chunks.
    /// Each chunk carries the heading path and page number from its source chapter.
    /// </summary>
    private static void SubdivideContent(
        string text,
        IReadOnlyList<string> headingPath,
        int pageNumber,
        List<IndexChunk> result)
    {
        // Split into paragraphs (separated by blank lines or newlines)
        var paragraphs = text.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries);

        if (paragraphs.Length == 0)
            return;

        var currentChunkParas = new List<string>();
        var currentCharCount = 0;
        var overlapBuffer = new List<string>(); // stores last few paragraphs for overlap
        var overlapCharCount = 0;

        // First, handle the single-paragraph case
        if (paragraphs.Length == 1)
        {
            var para = paragraphs[0].Trim();
            if (para.Length > 0)
            {
                result.Add(new IndexChunk
                {
                    Text = para,
                    HeadingPath = headingPath,
                    PageNumber = pageNumber,
                });
            }
            return;
        }

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (trimmed.Length == 0) continue;

            var paraLen = trimmed.Length;

            // If adding this paragraph would exceed the target, emit the current
            // chunk (if non-empty) and start a new one.
            if (currentCharCount + paraLen > TargetCharsPerChunk && currentCharCount > 0)
            {
                // Emit current batch
                result.Add(new IndexChunk
                {
                    Text = string.Join("\n", currentChunkParas),
                    HeadingPath = headingPath,
                    PageNumber = pageNumber,
                });

                // Build overlap: take the last N paragraphs that fit within OverlapChars
                overlapBuffer.Clear();
                overlapCharCount = 0;
                foreach (var overlapPara in currentChunkParas.AsEnumerable().Reverse())
                {
                    if (overlapCharCount + overlapPara.Length > OverlapChars && overlapBuffer.Count > 0)
                        break;
                    overlapBuffer.Insert(0, overlapPara);
                    overlapCharCount += overlapPara.Length;
                }

                // Start new chunk from overlap + current paragraph
                currentChunkParas = new List<string>(overlapBuffer);
                currentCharCount = overlapCharCount;
            }

            currentChunkParas.Add(trimmed);
            currentCharCount += paraLen;
        }

        // Emit remaining paragraphs
        if (currentChunkParas.Count > 0)
        {
            result.Add(new IndexChunk
            {
                Text = string.Join("\n", currentChunkParas),
                HeadingPath = headingPath,
                PageNumber = pageNumber,
            });
        }
    }

    /// <summary>
    /// Generic paragraph-based chunking for documents with no heading/page structure.
    /// Groups consecutive paragraphs into chunks near the target size.
    /// </summary>
    private static void ChunkParagraphs(
        IReadOnlyList<string> paragraphs,
        List<IndexChunk> result)
    {
        if (paragraphs.Count == 0) return;

        var currentParas = new List<string>();
        var currentCharCount = 0;
        var overlapBuffer = new List<string>();
        var overlapCharCount = 0;

        foreach (var para in paragraphs)
        {
            var trimmed = para.Trim();
            if (trimmed.Length == 0) continue;

            var paraLen = trimmed.Length;

            if (currentCharCount + paraLen > TargetCharsPerChunk && currentCharCount > 0)
            {
                result.Add(new IndexChunk
                {
                    Text = string.Join("\n", currentParas),
                });

                // Build overlap
                overlapBuffer.Clear();
                overlapCharCount = 0;
                foreach (var overlapPara in currentParas.AsEnumerable().Reverse())
                {
                    if (overlapCharCount + overlapPara.Length > OverlapChars && overlapBuffer.Count > 0)
                        break;
                    overlapBuffer.Insert(0, overlapPara);
                    overlapCharCount += overlapPara.Length;
                }

                currentParas = new List<string>(overlapBuffer);
                currentCharCount = overlapCharCount;
            }

            currentParas.Add(trimmed);
            currentCharCount += paraLen;
        }

        if (currentParas.Count > 0)
        {
            result.Add(new IndexChunk
            {
                Text = string.Join("\n", currentParas),
            });
        }
    }
}

/// <summary>
/// A single chunk of indexed content, ready to be written to the `chunks` table.
/// </summary>
public sealed class IndexChunk
{
    /// <summary>
    /// The chunk text content.
    /// </summary>
    public string Text { get; init; } = "";

    /// <summary>
    /// The heading path for this chunk, if any.
    /// </summary>
    public IReadOnlyList<string> HeadingPath { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The 0-based page number, if the source provides it; -1 otherwise.
    /// </summary>
    public int PageNumber { get; init; } = -1;
}