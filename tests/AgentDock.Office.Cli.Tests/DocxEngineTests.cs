using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AgentDock.Office.Cli.Tests;

public class DocxEngineTests : IDisposable
{
    private readonly string _dir;

    public DocxEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "office-cli-docx-tests-" + Guid.NewGuid());
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

    [Fact]
    public void Create_ThenReadText_RoundTripsTitleAndContent()
    {
        var file = PathFor("roundtrip.docx");

        DocxEngine.Create(file, "My Title", "Line one\nLine two");
        var text = DocxEngine.ReadText(file);

        Assert.Contains("My Title", text);
        Assert.Contains("Line one", text);
        Assert.Contains("Line two", text);
    }

    [Fact]
    public void Create_ThenReadText_PreservesBackslashesInContent()
    {
        var file = PathFor("backslash.docx");
        const string content = @"Path is C:\Users\test\file.txt done.";

        DocxEngine.Create(file, "Title", content);
        var text = DocxEngine.ReadText(file);

        Assert.Contains(content, text);
    }

    [Fact]
    public void Create_EmptyContent_ProducesOnlyTitleParagraph()
    {
        var file = PathFor("empty-content.docx");

        DocxEngine.Create(file, "Solo Title", "");
        var info = DocxEngine.GetInfo(file);

        Assert.Equal(1, info["paragraphCount"]);
    }

    [Fact]
    public void GetInfo_ReturnsCountsMatchingContent()
    {
        var file = PathFor("counts.docx");

        DocxEngine.Create(file, "Title", "one two three\nfour five");
        var info = DocxEngine.GetInfo(file);

        Assert.Equal(3, info["paragraphCount"]); // title + 2 content lines
        Assert.Equal(6, info["wordCount"]); // "Title" + 5 words across both lines
        Assert.False(info.ContainsKey("error"));
    }

    [Fact]
    public void GetInfo_DocumentWithNoBody_ThrowsInsteadOfReturningErrorInPayload()
    {
        // Errors must surface via exit code/stderr per the CLI contract (DEV_PLAN.md),
        // never folded into the JSON payload as a 200-equivalent "error" key.
        var file = PathFor("no-body.docx");
        using (var doc = WordprocessingDocument.Create(file, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(); // no Body child appended
            mainPart.Document.Save();
        }

        Assert.Throws<InvalidDataException>(() => DocxEngine.GetInfo(file));
    }
}
