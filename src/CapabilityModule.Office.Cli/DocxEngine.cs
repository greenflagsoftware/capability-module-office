using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CapabilityModule.Office.Cli;

/// <summary>
/// Core OpenXml operations for .docx documents — read text, create documents,
/// and extract metadata. All paths are assumed to be pre-validated by
/// <see cref="PathSecurity"/> before reaching this class.
/// </summary>
internal static class DocxEngine
{
    /// <summary>
    /// Performs a find-and-replace text substitution within a .docx document.
    /// Opens the file for editing, walks all paragraphs/runs, and replaces
    /// occurrences of <paramref name="findText"/> with <paramref name="replaceText"/>
    /// within each run's text content.
    ///
    /// Note: OpenXml does not offer a clean partial in-place text patch, so this
    /// rebuilds the whole document in memory when saved — see DEV_PLAN.md for
    /// the implementation detail vs. contract distinction.
    ///
    /// If <paramref name="findText"/> is split across multiple runs (e.g. due to
    /// formatting boundaries), it will not be matched — this is a known limitation
    /// of the per-run traversal approach.
    /// </summary>
    public static void ReplaceText(string filePath, string findText, string replaceText)
    {
        using var doc = WordprocessingDocument.Open(filePath, true);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is not Body body) return;

        foreach (var para in body.Elements<Paragraph>())
        {
            foreach (var run in para.Elements<Run>())
            {
                var text = run.GetFirstChild<Text>();
                if (text != null && text.Text.Contains(findText, StringComparison.Ordinal))
                {
                    text.Text = text.Text.Replace(findText, replaceText);
                }
            }
        }

        part.Document.Save();
    }
    /// <summary>
    /// Extracts all plain text from a .docx file, preserving paragraph breaks.
    /// </summary>
    public static string ReadText(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var part = doc.MainDocumentPart;
        if (part?.Document?.Body is not Body body) return string.Empty;

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText)
            .ToList();

        return string.Join(Environment.NewLine, paragraphs);
    }

    /// <summary>
    /// Creates a new .docx document with a title heading and body text paragraphs.
    /// </summary>
    public static void Create(string filePath, string title, string content)
    {
        using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Title paragraph (Heading1)
        var titlePara = new Paragraph(
            new Run(
                new RunProperties(
                    new Bold(),
                    new FontSize { Val = "28" },
                    new Color { Val = "1F3864" }),
                new Text(title)))
        {
            ParagraphProperties = new ParagraphProperties(
                new Justification { Val = JustificationValues.Left },
                new SpacingBetweenLines { After = "200" }),
        };
        body.AppendChild(titlePara);

        // Content paragraphs (split on newlines)
        if (!string.IsNullOrWhiteSpace(content))
        {
            var lines = content.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var para = new Paragraph(
                    new Run(
                        new RunProperties(
                            new FontSize { Val = "24" }),
                        new Text(line) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphProperties = new ParagraphProperties(
                        new SpacingBetweenLines { After = "120" }),
                };
                body.AppendChild(para);
            }
        }

        mainPart.Document.Save();
    }

    /// <summary>
    /// Returns metadata about a .docx document: paragraph count, word count,
    /// and character count.
    /// </summary>
    public static Dictionary<string, object?> GetInfo(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, false);
        var part = doc.MainDocumentPart;

        if (part?.Document?.Body is not Body body)
        {
            // Errors surface via exit code/stderr per the CLI contract (DEV_PLAN.md) —
            // never folded into the JSON payload. The caller (DocxCommand) catches this
            // and exits non-zero.
            throw new InvalidDataException($"'{filePath}' has no document body (malformed or empty .docx).");
        }

        var paragraphs = body.Elements<Paragraph>().ToList();
        var allText = string.Join(" ", paragraphs.Select(p => p.InnerText));
        var words = allText.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return new Dictionary<string, object?>
        {
            ["paragraphCount"] = paragraphs.Count,
            ["wordCount"] = words.Length,
            ["charCount"] = allText.Length,
        };
    }
}