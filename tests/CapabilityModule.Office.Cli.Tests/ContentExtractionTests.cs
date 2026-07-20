using CapabilityModule.Office.Cli.Extractors;

namespace CapabilityModule.Office.Cli.Tests;

public class ContentExtractionTests : IDisposable
{
    private readonly string _dir;

    public ContentExtractionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "office-cli-extraction-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    // ---------------------------------------------------------------
    // .docx extractor tests
    // ---------------------------------------------------------------

    [Fact]
    public void DocxExtractor_ExtractsText()
    {
        var file = PathFor("headings.docx");
        DocxEngine.Create(file, "Chapter 1", "Some body text here.\nMore details.");

        var extractor = new DocxExtractor();
        var result = extractor.Extract(file);

        Assert.Contains("Chapter 1", result.Text);
        Assert.Contains("Some body text here.", result.Text);
        Assert.Contains("More details.", result.Text);
        Assert.Equal(3, result.Paragraphs.Count); // title + 2 content lines
    }

    [Fact]
    public void DocxExtractor_HeadingStructureCreatesChapters()
    {
        // Create a document with proper Heading1/Heading2 paragraph styles
        var file = PathFor("multilevel.docx");
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());

            // Heading 1
            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = "Heading1" }),
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("Main Section"))));

            // Body under H1
            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("Text under main section."))));

            // Heading 2
            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = "Heading2" }),
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("Sub Section"))));

            // Body under H2
            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("Text under sub section."))));

            mainPart.Document.Save();
        }

        var extractor = new DocxExtractor();
        var result = extractor.Extract(file);

        // Should have 2 chapters (one per heading section)
        Assert.True(result.Chapters.Count >= 2, $"Expected at least 2 chapters, got {result.Chapters.Count}");

        // First chapter should have heading path ["Main Section"]
        Assert.Equal("Main Section", result.Chapters[0].HeadingPath.FirstOrDefault());

        // Second chapter should have heading path ["Main Section", "Sub Section"]
        Assert.Contains("Sub Section", result.Chapters[1].HeadingPath);

        // Check paragraph heading metadata
        Assert.True(result.ParagraphHeadings.ContainsKey(0)); // Heading1
        Assert.True(result.ParagraphHeadings.ContainsKey(2)); // Heading2
        Assert.Equal("Main Section", result.ParagraphHeadings[0][0]);
        Assert.Equal(2, result.ParagraphHeadings[2].Count); // ["Main Section", "Sub Section"]
    }

    [Fact]
    public void DocxExtractor_TableContentIsExtracted()
    {
        // A heading, a paragraph, then a table — tables are direct children of
        // Body alongside Paragraph, so body.Elements<Paragraph>() alone would
        // never see this table (regression test for the table-drop bug).
        var file = PathFor("with-table.docx");
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            var body = mainPart.Document.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Body());

            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = "Heading1" }),
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("Pricing"))));

            body.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text("See the table below."))));

            DocumentFormat.OpenXml.Wordprocessing.TableCell Cell(string text) =>
                new(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                    new DocumentFormat.OpenXml.Wordprocessing.Run(
                        new DocumentFormat.OpenXml.Wordprocessing.Text(text))));

            var table = new DocumentFormat.OpenXml.Wordprocessing.Table(
                new DocumentFormat.OpenXml.Wordprocessing.TableRow(Cell("Plan"), Cell("Price")),
                new DocumentFormat.OpenXml.Wordprocessing.TableRow(Cell("Basic"), Cell("$10")),
                new DocumentFormat.OpenXml.Wordprocessing.TableRow(Cell("Pro"), Cell("$25")));
            body.AppendChild(table);

            mainPart.Document.Save();
        }

        var extractor = new DocxExtractor();
        var result = extractor.Extract(file);

        // The table's content must appear somewhere in the extracted text —
        // previously it was silently dropped entirely.
        Assert.Contains("Basic", result.Text);
        Assert.Contains("$10", result.Text);
        Assert.Contains("Pro", result.Text);
        Assert.Contains("$25", result.Text);

        // The table should survive as one intact, recognizable unit (single
        // paragraph entry, no embedded newlines) rather than being flattened
        // row-by-row into ordinary prose paragraphs.
        var tableParagraph = result.Paragraphs.SingleOrDefault(p => p.StartsWith("[Table]"));
        Assert.NotNull(tableParagraph);
        Assert.DoesNotContain('\n', tableParagraph!);
        Assert.DoesNotContain('\r', tableParagraph!);
        Assert.Contains("Plan | Price", tableParagraph);
        Assert.Contains("Basic | $10", tableParagraph);
        Assert.Contains("Pro | $25", tableParagraph);

        // The table paragraph should carry the heading path in effect at its
        // position, same as any other paragraph.
        var tableIndex = result.Paragraphs.ToList().IndexOf(tableParagraph!);
        Assert.True(result.ParagraphHeadings.ContainsKey(tableIndex));
        Assert.Contains("Pricing", result.ParagraphHeadings[tableIndex]);
    }

    [Fact]
    public void DocxExtractor_EmptyBody_ReturnsEmptyDocument()
    {
        var file = PathFor("empty.docx");
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
            file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
            // No Body appended
            mainPart.Document.Save();
        }

        var extractor = new DocxExtractor();
        var result = extractor.Extract(file);

        Assert.Equal("", result.Text);
        Assert.Empty(result.Paragraphs);
    }

    // ---------------------------------------------------------------
    // Plain-text extractor tests
    // ---------------------------------------------------------------

    [Fact]
    public void PlainTextExtractor_ExtractsAllContent()
    {
        var file = PathFor("sample.txt");
        File.WriteAllText(file, "First line\n\nSecond paragraph\n\nThird paragraph with more text.");

        var extractor = new PlainTextExtractor();
        var result = extractor.Extract(file);

        Assert.Contains("First line", result.Text);
        Assert.Contains("Second paragraph", result.Text);
        Assert.Equal(3, result.Paragraphs.Count);
    }

    [Fact]
    public void PlainTextExtractor_EmptyFile_ReturnsEmpty()
    {
        var file = PathFor("empty.txt");
        File.WriteAllText(file, "");

        var extractor = new PlainTextExtractor();
        var result = extractor.Extract(file);

        Assert.Equal("", result.Text);
        Assert.Empty(result.Paragraphs);
        Assert.Empty(result.Chapters);
    }

    [Fact]
    public void PlainTextExtractor_SingleChapter_ContainsAllText()
    {
        var file = PathFor("single-chapter.txt");
        File.WriteAllText(file, "All content in one go.");

        var extractor = new PlainTextExtractor();
        var result = extractor.Extract(file);

        Assert.Single(result.Chapters);
        Assert.Equal("All content in one go.", result.Chapters[0].Text);
    }

    // ---------------------------------------------------------------
    // PDF extractor tests — using embedded minimal valid PDF fixtures
    // ---------------------------------------------------------------

    [Fact]
    public void PdfExtractor_ExtractsTextFromSimplePdf()
    {
        var file = PathFor("hello.pdf");
        File.WriteAllBytes(file, MinimalPdf("Hello World"));

        var extractor = new PdfExtractor();
        var result = extractor.Extract(file);

        Assert.Contains("Hello World", result.Text);
        // Single page PDF should produce one chapter
        Assert.Single(result.Chapters);
        Assert.Equal(0, result.Chapters[0].PageNumber);
    }

    [Fact]
    public void PdfExtractor_ExtractsMultiPageText()
    {
        var file = PathFor("multi-page.pdf");
        File.WriteAllBytes(file, TwoPagePdf("Page one content", "Page two content"));

        var extractor = new PdfExtractor();
        var result = extractor.Extract(file);

        Assert.Contains("Page one content", result.Text);
        Assert.Contains("Page two content", result.Text);
        // Two pages should produce two chapters
        Assert.Equal(2, result.Chapters.Count);
        Assert.Equal(0, result.Chapters[0].PageNumber);
        Assert.Equal(1, result.Chapters[1].PageNumber);
    }

    [Fact]
    public void PdfExtractor_ZeroExtractableText_Throws()
    {
        var file = PathFor("blank.pdf");
        // A PDF with a page but no text drawing operators
        File.WriteAllBytes(file, MinimalPdf(""));

        var extractor = new PdfExtractor();
        var ex = Assert.Throws<InvalidDataException>(() => extractor.Extract(file));
        Assert.Contains("no extractable text", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // Factory dispatch tests
    // ---------------------------------------------------------------

    [Fact]
    public void Factory_ReturnsDocxExtractor_ForDocxFiles()
    {
        var extractor = ContentExtractorFactory.GetExtractor("test.docx");
        Assert.IsType<DocxExtractor>(extractor);
    }

    [Fact]
    public void Factory_ReturnsPlainTextExtractor_ForTextFiles()
    {
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.txt"));
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.md"));
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.csv"));
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.json"));
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.xml"));
        Assert.IsType<PlainTextExtractor>(ContentExtractorFactory.GetExtractor("test.yaml"));
    }

    [Fact]
    public void Factory_ReturnsPdfExtractor_ForPdfFiles()
    {
        var extractor = ContentExtractorFactory.GetExtractor("test.pdf");
        Assert.IsType<PdfExtractor>(extractor);
    }

    [Fact]
    public void Factory_UnknownExtension_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => ContentExtractorFactory.GetExtractor("test.xyz"));
        Assert.Contains("no content extractor available", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Factory_NoExtension_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => ContentExtractorFactory.GetExtractor("README"));
        Assert.Contains("no extension", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------
    // PDF fixture generation
    // ---------------------------------------------------------------

    /// <summary>
    /// Generates a minimal valid single-page PDF with the given text drawn on it.
    /// Built manually byte-by-byte to guarantee correctness for PdfPig.
    /// </summary>
    private static byte[] MinimalPdf(string text)
    {
        // We produce content string using PDF string escaping. If text is empty,
        // we still produce a valid page with an empty content stream.
        var content = text.Length > 0
            ? $"BT /F1 12 Tf 100 700 Td ({EscapePdfString(text)}) Tj ET"
            : "";

        return BuildPdf(new[] { content });
    }

    /// <summary>
    /// Generates a minimal valid two-page PDF with different text on each page.
    /// </summary>
    private static byte[] TwoPagePdf(string page1Text, string page2Text)
    {
        return BuildPdf(new[]
        {
            $"BT /F1 12 Tf 100 700 Td ({EscapePdfString(page1Text)}) Tj ET",
            $"BT /F1 12 Tf 100 700 Td ({EscapePdfString(page2Text)}) Tj ET",
        });
    }

    private static string EscapePdfString(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("(", "\\(")
            .Replace(")", "\\)")
            .Replace("\n", "\\n");
    }

    /// <summary>
    /// Builds a valid PDF from content stream strings.
    /// Object layout:
    ///   1 0: Catalog
    ///   2 0: Pages
    ///   3 0..2+N: Content stream objects (N of them)
    ///   3+N: Font
    ///   4+N..3+2N: Page objects
    /// </summary>
    private static byte[] BuildPdf(string[] streamContents)
    {
        var n = streamContents.Length;
        // Object IDs (1-based)
        const int catalogId = 1;
        const int pagesId = 2;
        var streamBaseId = 3;
        var fontId = streamBaseId + n;               // 3 + N
        var firstPageId = fontId + 1;                 // 4 + N
        // Last page ID = firstPageId + N - 1 = 3 + 2N

        var lines = new List<string>();
        lines.Add("%PDF-1.4");

        // Helper to track byte offset of each object
        long Pos() => System.Text.Encoding.ASCII.GetByteCount(string.Join("\n", lines)) + 1;
        var offsets = new Dictionary<int, long>();

        // 1 0 obj — Catalog
        offsets[catalogId] = Pos();
        lines.Add("1 0 obj");
        lines.Add("<< /Type /Catalog /Pages 2 0 R >>");
        lines.Add("endobj");

        // 2 0 obj — Pages (parent of all page objects)
        offsets[pagesId] = Pos();
        var kidRefs = string.Join(" ",
            Enumerable.Range(0, n).Select(i => $"{firstPageId + i} 0 R"));
        lines.Add("2 0 obj");
        lines.Add($"<< /Type /Pages /Kids [{kidRefs}] /Count {n} >>");
        lines.Add("endobj");

        // Content stream objects (3..2+N)
        var contentStreamIds = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var streamId = streamBaseId + i;
            contentStreamIds.Add(streamId);
            var streamBytes = System.Text.Encoding.ASCII.GetBytes(streamContents[i]);

            offsets[streamId] = Pos();
            lines.Add($"{streamId} 0 obj");
            lines.Add($"<< /Length {streamBytes.Length} >>");
            lines.Add("stream");
            lines.Add(streamContents[i]);
            lines.Add("endstream");
            lines.Add("endobj");
        }

        // Font object (3+N)
        offsets[fontId] = Pos();
        lines.Add($"{fontId} 0 obj");
        lines.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        lines.Add("endobj");

        // Page objects (4+N .. 3+2N)
        for (var i = 0; i < n; i++)
        {
            var pageId = firstPageId + i;
            offsets[pageId] = Pos();
            lines.Add($"{pageId} 0 obj");
            lines.Add(
                $"<< /Type /Page /Parent {pagesId} 0 R " +
                $"/MediaBox [0 0 612 792] " +
                $"/Contents {contentStreamIds[i]} 0 R " +
                $"/Resources << /Font << /F1 {fontId} 0 R >> >> >>");
            lines.Add("endobj");
        }

        // xref table
        var xrefOffset = Pos();
        var totalObjects = firstPageId + n; // = 4 + 2N
        lines.Add("xref");
        lines.Add($"0 {totalObjects}");
        lines.Add("0000000000 65535 f ");
        for (var id = 1; id < totalObjects; id++)
        {
            lines.Add($"{offsets[id]:D10} 00000 n ");
        }

        lines.Add("trailer");
        lines.Add($"<< /Size {totalObjects} /Root {catalogId} 0 R >>");
        lines.Add("startxref");
        lines.Add(xrefOffset.ToString());
        lines.Add("%%EOF");

        return System.Text.Encoding.ASCII.GetBytes(string.Join("\n", lines));
    }
}