using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Agent;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// একই ফাইল দুবার আপলোড হলে হুবহু একই chunk একাধিকবার আসে; dedupe না থাকলে তারা top-N দখল করে বাকি তথ্য বের করে দেয়।
public class KnowledgeBaseSearchToolTests
{
    [Fact]
    public async Task DuplicateChunks_AreCollapsed_SoOtherPassagesReachTheModel()
    {
        var retriever = Substitute.For<IRetriever>();
        retriever.RetrieveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>(
            [
                new RetrievedChunk("resignation: notice is two months", 0.9, "kb.md"),
                new RetrievedChunk("resignation: notice is two months", 0.9, "kb.md"),
                new RetrievedChunk("resignation: notice is two months", 0.9, "kb.md"),
                new RetrievedChunk("salary: paid by the 10th", 0.8, "kb.md"),
            ]));
        var resolver = Substitute.For<IRetrieverResolver>();
        resolver.Resolve("qdrant").Returns(retriever);
        var trace = Substitute.For<IAgentRetrievalTrace>();
        var tool = new KnowledgeBaseSearchTool(resolver, trace);

        using var args = JsonDocument.Parse("""{"query":"resign and salary"}""");
        var output = await tool.ExecuteAsync(args.RootElement, CancellationToken.None);

        Assert.Contains("salary: paid by the 10th", output);
        Assert.Equal(1, CountOf(output, "resignation: notice is two months"));
        trace.Received().SetDocumentHits(Arg.Is<IReadOnlyList<ScoredHit>>(h => h.Count == 2));
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            count++;
        return count;
    }
}
