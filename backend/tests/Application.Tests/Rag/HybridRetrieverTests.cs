using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Rag;

namespace AiPlatform.Application.Tests.Rag;

public class HybridRetrieverTests
{
    private static RetrievedChunk C(string text) => new(text, null, "kb");

    [Fact]
    public void Fuse_DocInBothLists_OutranksDocInOneList()
    {
        // "B" keyword-এ rank 2, embedding-এ rank 1 → দুই contribution → top।
        var keyword = new[] { C("A"), C("B") };
        var embedding = new[] { C("B"), C("C") };

        var result = HybridRetriever.Fuse([keyword, embedding], topN: 3);

        Assert.Equal("B", result[0].Text);
    }

    [Fact]
    public void Fuse_DedupesByText_AndRespectsTopN()
    {
        var keyword = new[] { C("A"), C("B"), C("C") };
        var embedding = new[] { C("B"), C("C"), C("D") };

        var result = HybridRetriever.Fuse([keyword, embedding], topN: 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(result.Select(r => r.Text).Distinct().Count(), result.Count);
    }

    [Fact]
    public void Fuse_EmptySource_StillFusesOther()
    {
        var result = HybridRetriever.Fuse([Array.Empty<RetrievedChunk>(), new[] { C("A") }], topN: 3);

        Assert.Single(result);
        Assert.Equal("A", result[0].Text);
    }
}
