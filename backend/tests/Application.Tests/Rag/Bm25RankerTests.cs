using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Rag;
using Xunit;

namespace AiPlatform.Application.Tests.Rag;

public class Bm25RankerTests
{
    private static RetrievedChunk Chunk(string text) => new(text, null, "kb.md");

    [Fact]
    public void RareQueryTerm_RanksItsChunkFirst()
    {
        var docs = new[]
        {
            Chunk("Marketing team: Shiplu and Likhon"),
            Chunk("Support team members work in shifts"),
            Chunk("Development team builds the product"),
        };

        var ranked = Bm25Ranker.Rank("who is Shiplu", docs, topN: 3);

        Assert.Equal("Marketing team: Shiplu and Likhon", ranked[0].Text);
    }

    [Fact]
    public void ChunksWithNoMatchingTerm_AreExcluded()
    {
        var docs = new[] { Chunk("alpha beta"), Chunk("gamma delta") };

        var ranked = Bm25Ranker.Rank("alpha", docs, topN: 5);

        Assert.Single(ranked);
        Assert.Equal("alpha beta", ranked[0].Text);
    }

    [Fact]
    public void EmptyQueryOrCorpus_ReturnsEmpty()
    {
        Assert.Empty(Bm25Ranker.Rank("", [Chunk("alpha")], 3));
        Assert.Empty(Bm25Ranker.Rank("alpha", [], 3));
    }

    // বাংলা যুক্তাক্ষর/স্বরচিহ্ন শব্দের ভেতরে থাকতে হবে, নইলে "কর্মদিবস" টুকরো হয়ে যায়।
    [Fact]
    public void Tokenize_KeepsBengaliWordsIntact()
    {
        var tokens = Bm25Ranker.Tokenize("শেষ কর্মদিবস কবে? Office-Hours 10:00");

        Assert.Contains("কর্মদিবস", tokens);
        Assert.Contains("শেষ", tokens);
        Assert.Contains("office", tokens);
        Assert.Contains("hours", tokens);
        Assert.Contains("10", tokens);
    }

    [Fact]
    public void TopN_LimitsResults_BestFirst()
    {
        var docs = new[] { Chunk("notice notice notice"), Chunk("notice"), Chunk("notice other words here") };

        var ranked = Bm25Ranker.Rank("notice", docs, topN: 2);

        Assert.Equal(2, ranked.Count);
        Assert.True(ranked[0].Score >= ranked[1].Score);
    }
}
