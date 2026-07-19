using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace CapabilityModule.Office.Cli.Extractors;

/// <summary>
/// PDF content extractor using PdfPig. Extracts text and page-level structure
/// from PDFs that have an embedded text layer. Scanned/image-only PDFs (no
/// extractable text) are flagged distinctly.
/// </summary>
internal sealed class PdfExtractor : IContentExtractor
{
    public NormalizedDocument Extract(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);

        var pageTexts = new List<string>();
        var paragraphs = new List<string>();

        foreach (var page in pdf.GetPages())
        {
            var pageText = page.Text;
            pageTexts.Add(pageText);

            // Split page text into paragraphs on blank lines
            var pageParagraphs = pageText
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0);

            paragraphs.AddRange(pageParagraphs);
        }

        var allText = string.Join(Environment.NewLine, pageTexts);

        // Check for zero extractable text
        if (string.IsNullOrWhiteSpace(allText))
        {
            throw new InvalidDataException(
                $"'{filePath}' contains no extractable text. This may be a scanned/image-only PDF.");
        }

        // Build chapters: one per page
        var chapters = new List<ContentChunk>();
        for (var i = 0; i < pageTexts.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(pageTexts[i]))
            {
                chapters.Add(new ContentChunk
                {
                    Text = pageTexts[i],
                    PageNumber = i, // 0-based
                });
            }
        }

        return new NormalizedDocument
        {
            Text = allText,
            Paragraphs = paragraphs,
            Chapters = chapters,
        };
    }
}