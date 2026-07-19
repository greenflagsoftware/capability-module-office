namespace CapabilityModule.Office.Cli.Extractors;

/// <summary>
/// Plain-text content extractor. Divides text into paragraphs (splitting on
/// blank lines) and treats the entire document as a single chapter with no
/// heading structure.
/// </summary>
internal sealed class PlainTextExtractor : IContentExtractor
{
    public NormalizedDocument Extract(string filePath)
    {
        var allText = File.ReadAllText(filePath);
        if (string.IsNullOrEmpty(allText))
            return new NormalizedDocument();

        // Split on blank lines to recover paragraph boundaries, then trim
        // each paragraph. A "blank line" is a line that is empty or whitespace-only.
        var paragraphs = allText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        return new NormalizedDocument
        {
            Text = allText,
            Paragraphs = paragraphs,
            Chapters = new[]
            {
                new ContentChunk
                {
                    Text = allText,
                },
            },
        };
    }
}