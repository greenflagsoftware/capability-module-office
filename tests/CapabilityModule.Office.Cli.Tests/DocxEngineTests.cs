using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace CapabilityModule.Office.Cli.Tests;

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

    [Fact]
    public void ReplaceText_SimpleFindReplace_SubstitutesCorrectly()
    {
        var file = PathFor("replace-simple.docx");
        DocxEngine.Create(file, "Title", "Hello world, this is a test.");

        DocxEngine.ReplaceText(file, "world", "universe");
        var text = DocxEngine.ReadText(file);

        Assert.Contains("Hello universe", text);
        Assert.DoesNotContain("Hello world", text);
    }

    [Fact]
    public void ReplaceText_NoMatch_DoesNotChangeContent()
    {
        var file = PathFor("replace-nomatch.docx");
        DocxEngine.Create(file, "Title", "This is the original content.");

        var before = DocxEngine.ReadText(file);
        DocxEngine.ReplaceText(file, "nonexistent", "replacement");
        var after = DocxEngine.ReadText(file);

        Assert.Equal(before, after);
    }

    [Fact]
    public void ReplaceText_MultipleOccurrences_ReplacesAll()
    {
        var file = PathFor("replace-multiple.docx");
        DocxEngine.Create(file, "Title", "foo foo foo");

        DocxEngine.ReplaceText(file, "foo", "bar");
        var text = DocxEngine.ReadText(file);

        Assert.Equal(3, text.Split("bar").Length - 1); // 3 occurrences of "bar"
        Assert.DoesNotContain("foo", text);
    }

    [Fact]
    public void ReplaceText_EmptyReplacement_DeletesMatches()
    {
        var file = PathFor("replace-delete.docx");
        DocxEngine.Create(file, "Title", "Keep this part and remove this part.");

        DocxEngine.ReplaceText(file, "part", "");
        var text = DocxEngine.ReadText(file);

        Assert.Contains("Keep this  and remove this .", text);
    }

    [Fact]
    public void ReplaceText_ReplaceInTitle_Works()
    {
        var file = PathFor("replace-title.docx");
        DocxEngine.Create(file, "My Document Title", "Body content.");

        DocxEngine.ReplaceText(file, "Document", "Report");
        var text = DocxEngine.ReadText(file);

        Assert.Contains("My Report Title", text);
        Assert.DoesNotContain("My Document Title", text);
    }

    [Fact]
    public void ReplaceText_NonMatchingContent_IsVerifiablyUnchanged()
    {
        // This is the check that distinguishes edit correctness from create's:
        // non-matching paragraphs should be untouched.
        var file = PathFor("replace-unchanged-check.docx");
        DocxEngine.Create(file, "Title", "First paragraph.\nSecond paragraph with change.\nThird paragraph.");

        DocxEngine.ReplaceText(file, "Second paragraph with change", "Second paragraph was changed");

        var text = DocxEngine.ReadText(file);
        Assert.Contains("First paragraph.", text);
        Assert.Contains("Second paragraph was changed", text);
        Assert.Contains("Third paragraph.", text);
    }

    [Fact]
    public void ReplaceText_BackslashesInContent_RoundTrips()
    {
        var file = PathFor("replace-backslash.docx");
        DocxEngine.Create(file, "Title", @"Path is C:\Users\test\file.txt");

        DocxEngine.ReplaceText(file, "test", "prod");
        var text = DocxEngine.ReadText(file);

        Assert.Contains(@"C:\Users\prod\file.txt", text);
    }
}
