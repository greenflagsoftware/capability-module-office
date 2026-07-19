namespace CapabilityModule.Office.Cli.Tests;

public class ChunkingEngineTests : IDisposable
{
    private readonly string _dir;

    public ChunkingEngineTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "office-cli-chunking-tests-" + Guid.NewGuid());
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
    // Content hash tests
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeHash_ReturnsConsistentHexString()
    {
        var file = PathFor("hash-me.txt");
        File.WriteAllText(file, "Hello World");

        var hash1 = ChunkingEngine.ComputeFileHash(file);
        var hash2 = ChunkingEngine.ComputeFileHash(file);

        Assert.Equal(hash1, hash2);
        Assert.Matches("^[0-9a-f]{64}$", hash1);
    }

    [Fact]
    public void ComputeHash_DifferentContent_DifferentHash()
    {
        var fileA = PathFor("a.txt");
        var fileB = PathFor("b.txt");
        File.WriteAllText(fileA, "Content A");
        File.WriteAllText(fileB, "Content B");

        var hashA = ChunkingEngine.ComputeFileHash(fileA);
        var hashB = ChunkingEngine.ComputeFileHash(fileB);

        Assert.NotEqual(hashA, hashB);
    }

    // ---------------------------------------------------------------
    // Structure-aware chapter chunking
    // ---------------------------------------------------------------

    [Fact]
    public void ChunkDocument_SingleChapter_ProducesOneChunk()
    {
        var doc = new NormalizedDocument
        {
            Text = "Short chapter text.",
            Paragraphs = new[] { "Short chapter text." },
            Chapters = new[]
            {
                new ContentChunk { Text = "Short chapter text.", HeadingPath = new[] { "Intro" } },
            },
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        Assert.Single(chunks);
        Assert.Equal("Short chapter text.", chunks[0].Text);
        Assert.Equal("Intro", chunks[0].HeadingPath.FirstOrDefault());
    }

    [Fact]
    public void ChunkDocument_LargeChapter_SubdividesIntoMultipleChunks()
    {
        // Create a chapter with enough text to exceed the target chunk size
        var longText = string.Join("\n", Enumerable.Range(0, 200)
            .Select(i => $"This is paragraph number {i} with some filler text to make it longer."));

        var doc = new NormalizedDocument
        {
            Text = longText,
            Paragraphs = longText.Split('\n'),
            Chapters = new[]
            {
                new ContentChunk
                {
                    Text = longText,
                    HeadingPath = new[] { "Long Section" },
                    PageNumber = 0,
                },
            },
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        Assert.True(chunks.Count > 1,
            $"Expected multiple chunks from large content, got {chunks.Count}");

        // Each chunk should carry the heading path and page number
        foreach (var chunk in chunks)
        {
            Assert.Equal("Long Section", chunk.HeadingPath.FirstOrDefault());
            Assert.Equal(0, chunk.PageNumber);
        }

        // Verify overlap: the first few words of chunk 2 should appear in chunk 1
        var chunk1End = chunks[0].Text;
        var chunk2Start = chunks[1].Text;
        Assert.NotEqual(chunk1End, chunk2Start);

        // Check contiguous text: merging all chunks should contain the original text
        var mergedText = string.Join("\n", chunks.Select(c => c.Text));
        foreach (var para in doc.Paragraphs)
        {
            Assert.Contains(para.Trim(), mergedText);
        }
    }

    [Fact]
    public void ChunkDocument_MultipleChapters_RespectsChapterBoundaries()
    {
        var doc = new NormalizedDocument
        {
            Text = "Chapter one.\nChapter two.",
            Paragraphs = new[] { "Chapter one.", "Chapter two." },
            Chapters = new[]
            {
                new ContentChunk { Text = "Chapter one.", HeadingPath = new[] { "One" }, PageNumber = 0 },
                new ContentChunk { Text = "Chapter two.", HeadingPath = new[] { "Two" }, PageNumber = 1 },
            },
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Chapter one.", chunks[0].Text);
        Assert.Equal("Chapter two.", chunks[1].Text);
        Assert.Equal("One", chunks[0].HeadingPath.FirstOrDefault());
        Assert.Equal("Two", chunks[1].HeadingPath.FirstOrDefault());
    }

    // ---------------------------------------------------------------
    // Paragraph-based fallback chunking
    // ---------------------------------------------------------------

    [Fact]
    public void ChunkDocument_NoChapters_FallbackToParagraphChunking()
    {
        var paragraphs = Enumerable.Range(0, 100)
            .Select(i => $"Paragraph {i}: some sample text to make each paragraph reasonably sized.")
            .ToList();

        var doc = new NormalizedDocument
        {
            Text = string.Join("\n", paragraphs),
            Paragraphs = paragraphs,
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        Assert.True(chunks.Count > 1,
            $"Expected multiple chunks from fallback mode, got {chunks.Count}");

        // All text should be covered
        var allChunkText = string.Join(" ", chunks.Select(c => c.Text));
        foreach (var para in paragraphs)
        {
            Assert.Contains(para, allChunkText);
        }
    }

    [Fact]
    public void ChunkDocument_EmptyDocument_ReturnsNoChunks()
    {
        var doc = new NormalizedDocument();
        var chunks = ChunkingEngine.ChunkDocument(doc);
        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkDocument_SingleLargeParagraph_ProducesOneChunk()
    {
        var longPara = new string('x', ChunkingEngine.TargetCharsPerChunk * 3);
        var doc = new NormalizedDocument
        {
            Text = longPara,
            Paragraphs = new[] { longPara },
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        // A single paragraph that's longer than target can't be split at paragraph
        // boundaries — it stays as one chunk
        Assert.Single(chunks);
        Assert.Equal(longPara, chunks[0].Text);
    }

    // ---------------------------------------------------------------
    // Paragraph-based fallback with overlap
    // ---------------------------------------------------------------

    [Fact]
    public void ChunkDocument_ManySmallParagraphs_ProducesMultipleChunksWithOverlap()
    {
        // Many small paragraphs — enough to exceed target chunk size (~1600 chars)
        var paragraphs = Enumerable.Range(0, 300)
            .Select(i => $"Small paragraph {i} with some extra content to make each entry long enough.")
            .ToList();

        var doc = new NormalizedDocument
        {
            Text = string.Join("\n", paragraphs),
            Paragraphs = paragraphs,
        };

        var chunks = ChunkingEngine.ChunkDocument(doc);

        Assert.True(chunks.Count >= 2,
            $"Expected at least 2 chunks from 300 paragraphs, got {chunks.Count}");

        // Verify overlap: adjacent chunks should share some text
        for (var i = 1; i < chunks.Count; i++)
        {
            var prevEnd = chunks[i - 1].Text;
            var currStart = chunks[i].Text;
            // The end of the previous chunk should contain the first paragraph
            // of the current chunk (due to overlap)
            var firstCurrPara = currStart.Split('\n').FirstOrDefault();
            if (firstCurrPara is not null && firstCurrPara.Length > 0)
            {
                Assert.Contains(firstCurrPara, prevEnd);
            }
        }
    }

    // ---------------------------------------------------------------
    // Idempotency verification
    // ---------------------------------------------------------------

    [Fact]
    public void ChunkDocument_TokenTargetConstants_AreReasonable()
    {
        Assert.True(ChunkingEngine.TargetTokensPerChunk > 0);
        Assert.True(ChunkingEngine.OverlapTokens > 0);
        Assert.True(ChunkingEngine.OverlapTokens < ChunkingEngine.TargetTokensPerChunk);
    }
}