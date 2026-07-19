using CapabilityModule.Office.Cli.Extractors;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Selects the appropriate <see cref="IContentExtractor"/> for a given file path
/// based on its extension. Unknown extensions produce a clear error.
/// </summary>
internal static class ContentExtractorFactory
{
    private static readonly Dictionary<string, IContentExtractor> Extractors = new(
        StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = new DocxExtractor(),
        [".txt"] = new PlainTextExtractor(),
        [".md"] = new PlainTextExtractor(),
        [".csv"] = new PlainTextExtractor(),
        [".json"] = new PlainTextExtractor(),
        [".xml"] = new PlainTextExtractor(),
        [".yaml"] = new PlainTextExtractor(),
        [".yml"] = new PlainTextExtractor(),
        [".pdf"] = new PdfExtractor(),
    };

    /// <summary>
    /// Returns the content extractor for the given file path.
    /// </summary>
    /// <param name="filePath">The absolute file path to extract from.</param>
    /// <returns>The matching <see cref="IContentExtractor"/>.</returns>
    /// <exception cref="InvalidDataException">Thrown if no extractor is registered for the file's extension.</exception>
    public static IContentExtractor GetExtractor(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
        {
            throw new InvalidDataException(
                $"No content extractor available for file '{filePath}': file has no extension.");
        }

        if (Extractors.TryGetValue(extension, out var extractor))
            return extractor;

        throw new InvalidDataException(
            $"No content extractor available for '.{extension}' files. Supported formats: " +
            string.Join(", ", Extractors.Keys.Select(e => e.TrimStart('.'))));
    }
}