using AiPlatform.Application.Common.Text;
using Xunit;

namespace AiPlatform.Application.Tests.Text;

public class TextChunkerTests
{
    [Fact]
    public void ShortText_ReturnsSingleChunk()
    {
        var chunks = TextChunker.Chunk("short text here", maxChars: 200);
        Assert.Single(chunks);
        Assert.Equal("short text here", chunks[0]);
    }

    [Fact]
    public void LongText_SplitsIntoMultipleChunks()
    {
        var text = string.Join(" ", Enumerable.Range(1, 100).Select(i => $"word{i}"));
        var chunks = TextChunker.Chunk(text, maxChars: 50, overlapWords: 3);
        Assert.True(chunks.Count > 1);
    }

    [Fact]
    public void ConsecutiveChunks_ShareOverlapWords()
    {
        var text = string.Join(" ", Enumerable.Range(1, 60).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(text, maxChars: 40, overlapWords: 4);

        var firstWords = chunks[0].Split(' ');
        var secondWords = chunks[1].Split(' ');
        // দ্বিতীয় chunk প্রথমটার শেষ কিছু শব্দ দিয়ে শুরু (context ধরে রাখে)
        Assert.Contains(firstWords[^1], secondWords);
    }

    // ---- ChunkWithContext ----
    // আসল ব্যর্থতা: "- **Chief Executive Officer:** Rashed Kamal Hijal" লাইনে কোম্পানির নাম নেই,
    // তাই "Who is the CEO of Databiz?" প্রশ্নে সে হেরে যেত নাম-ভরা অথচ তথ্যহীন chunk-এর কাছে।

    private const string Doc = """
        --- title: Databiz Company Knowledge Base ---
        # Databiz
        ## 1. Company Overview
        - **Chief Executive Officer:** Rashed Kamal Hijal.
        ## 3. Office Schedule
        - **Weekly holidays:** Friday and Saturday.
        """;

    [Fact]
    public void ChunkWithContext_PutsFileNameAndHeadingTrailOnEveryChunk()
    {
        var chunks = TextChunker.ChunkWithContext(Doc, "Databiz_RAG_Knowledge_Base", maxChars: 1000);

        var ceo = Assert.Single(chunks, c => c.Contains("Rashed Kamal Hijal"));
        Assert.StartsWith("[Databiz_RAG_Knowledge_Base > Databiz > 1. Company Overview] ", ceo);
    }

    [Fact]
    public void ChunkWithContext_DropsDeeperHeadingsWhenASiblingStarts()
    {
        // "## 3. Office Schedule"-এ পৌঁছে "1. Company Overview" আর prefix-এ থাকা উচিত নয়।
        var chunks = TextChunker.ChunkWithContext(Doc, "kb", maxChars: 1000);

        var holidays = chunks.First(c => c.Contains("Friday"));
        Assert.Contains("3. Office Schedule", holidays);
        Assert.DoesNotContain("1. Company Overview", holidays);
    }

    [Fact]
    public void ChunkWithContext_StartsANewChunkAtEveryHeading()
    {
        // এক chunk দুই section জুড়ে থাকলে prefix মিথ্যা বলত, তাই heading-এ কাটা বাধ্যতামূলক।
        var chunks = TextChunker.ChunkWithContext(Doc, "kb", maxChars: 1000);

        Assert.DoesNotContain(chunks, c => c.Contains("Rashed") && c.Contains("Friday"));
    }

    [Fact]
    public void ChunkWithContext_PrefixDoesNotEatTheChunkBudget()
    {
        var body = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"w{i}"));

        var plain = TextChunker.Chunk(body, maxChars: 100, overlapWords: 3);
        var prefixed = TextChunker.ChunkWithContext(body, "a-very-long-document-title", maxChars: 100, overlapWords: 3);

        // prefix বাজেটের বাইরে — তাই টুকরোর সংখ্যা একই, শুধু মাথায় লেখা যোগ হয়।
        Assert.Equal(plain.Count, prefixed.Count);
        Assert.Equal("[a-very-long-document-title] " + plain[0], prefixed[0]);
    }

    [Fact]
    public void ChunkWithContext_IgnoresHashesThatAreNotHeadings()
    {
        var chunks = TextChunker.ChunkWithContext("see #tag and C# code", "kb", maxChars: 1000);

        Assert.Equal("[kb] see #tag and C# code", Assert.Single(chunks));
    }

    [Fact]
    public void Chunk_WithoutTitle_IsUnchangedByHeadingSupport()
    {
        // seed knowledge base এই পথেই চলে — আচরণ আগের মতোই থাকতে হবে।
        var chunks = TextChunker.Chunk("# Heading\nbody text here", maxChars: 1000);

        Assert.Equal("# Heading body text here", Assert.Single(chunks));
    }
}
