using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CapabilityModule.Office.Cli.Extractors;

/// <summary>
/// .docx content extractor that conforms the existing <see cref="DocxEngine"/>
/// extraction to the <see cref="IContentExtractor"/> interface, adding recoverable
/// heading structure from paragraph styles.
/// </summary>
internal sealed class DocxExtractor : IContentExtractor
{
    public NormalizedDocument Extract(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is not Body body)
            return new NormalizedDocument();

        var paragraphs = body.Elements<Paragraph>().ToList();

        var paragraphTexts = new List<string>();
        var paragraphHeadings = new Dictionary<int, IReadOnlyList<string>>();
        var headingStack = new List<string>();
        var headingIndices = new HashSet<int>(); // which paragraph indices are actual headings

        foreach (var (para, index) in paragraphs.Select((p, i) => (p, i)))
        {
            var text = para.InnerText;
            paragraphTexts.Add(text);

            var styleName = GetParagraphStyle(para);
            if (styleName is not null && TryGetHeadingLevel(styleName, out var level))
            {
                headingIndices.Add(index);

                // Trim the stack to the current heading level
                // (e.g., Heading2 under a different Heading1 pops back to level 0)
                while (headingStack.Count > level)
                    headingStack.RemoveAt(headingStack.Count - 1);

                if (headingStack.Count == level)
                    headingStack.Add(text);
                else
                    headingStack[level] = text;
            }

            if (headingStack.Count > 0)
            {
                paragraphHeadings[index] = headingStack.ToArray();
            }
        }

        var allText = string.Join(Environment.NewLine, paragraphTexts);

        // Build chapters based on heading boundaries
        var chapters = BuildChapters(paragraphTexts, headingIndices, paragraphHeadings);

        return new NormalizedDocument
        {
            Text = allText,
            Paragraphs = paragraphTexts,
            ParagraphHeadings = paragraphHeadings,
            Chapters = chapters,
        };
    }

    /// <summary>
    /// Splits paragraphs into chapters at heading boundaries. Each chapter
    /// starts at a heading paragraph and includes all following paragraphs
    /// until the next heading of the same or higher level.
    /// </summary>
    private static IReadOnlyList<ContentChunk> BuildChapters(
        IReadOnlyList<string> paragraphTexts,
        HashSet<int> headingIndices,
        IReadOnlyDictionary<int, IReadOnlyList<string>> paragraphHeadings)
    {
        var chapters = new List<ContentChunk>();
        var chapterStart = 0;
        IReadOnlyList<string> chapterHeading = Array.Empty<string>();

        for (var i = 0; i < paragraphTexts.Count; i++)
        {
            bool isHeading = headingIndices.Contains(i);

            if (isHeading)
            {
                if (i == 0)
                {
                    // First paragraph is a heading — capture its path but
                    // don't emit a prior chapter (there is none).
                    chapterHeading = paragraphHeadings[i];
                }
                else
                {
                    // Emit the previous chapter (from chapterStart to just
                    // before this heading) with its heading path.
                    var chapterText = string.Join(
                        Environment.NewLine,
                        paragraphTexts.Skip(chapterStart).Take(i - chapterStart));
                    if (!string.IsNullOrWhiteSpace(chapterText))
                    {
                        chapters.Add(new ContentChunk
                        {
                            Text = chapterText,
                            HeadingPath = chapterHeading,
                        });
                    }
                    chapterStart = i;
                    chapterHeading = paragraphHeadings[i];
                }
            }
        }

        // Emit the last chapter
        if (chapterStart < paragraphTexts.Count)
        {
            var chapterText = string.Join(
                Environment.NewLine,
                paragraphTexts.Skip(chapterStart));
            if (!string.IsNullOrWhiteSpace(chapterText))
            {
                chapters.Add(new ContentChunk
                {
                    Text = chapterText,
                    HeadingPath = chapterHeading,
                });
            }
        }

        // If no headings were found, treat the entire document as one chapter
        if (chapters.Count == 0 && paragraphTexts.Count > 0)
        {
            chapters.Add(new ContentChunk
            {
                Text = string.Join(Environment.NewLine, paragraphTexts),
            });
        }

        return chapters;
    }

    private static string? GetParagraphStyle(Paragraph para)
    {
        var pp = para.ParagraphProperties;
        if (pp?.ParagraphStyleId is null)
            return null;

        return pp.ParagraphStyleId.Val?.Value;
    }

    private static bool TryGetHeadingLevel(string styleName, out int level)
    {
        if (styleName.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(styleName.AsSpan("Heading".Length), out var l) &&
            l >= 1 && l <= 9)
        {
            level = l - 1; // 0-based
            return true;
        }

        level = 0;
        return false;
    }
}