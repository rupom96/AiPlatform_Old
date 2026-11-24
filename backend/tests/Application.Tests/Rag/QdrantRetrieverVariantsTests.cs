using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Rag;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Rag;

// hybrid (vector + BM25) ও rerank — default পথ (QdrantRetriever) অপরিবর্তিত থাকে, এরা তার উপরে বসে।
public class QdrantRetrieverVariantsTests
{
    private readonly IVectorStore _store = Substitute.For<IVectorStore>();
    private readonly IEmbeddingProvider _embedding = Substitute.For<IEmbeddingProvider>();
    private readonly IReranker _reranker = Substitute.For<IReranker>();
    private static readonly IOptions<RerankerSettings> Options =
        Microsoft.Extensions.Options.Options.Create(new RerankerSettings { Enabled = true, Candidates = 20 });

    private QdrantRetriever Vector()
    {
        _embedding.EmbedAsync(Arg.Any<string>(), Arg.Any<EmbeddingTaskType>(), Arg.Any<CancellationToken>())
            .Returns(new float[] { 0.1f, 0.2f });
        return new QdrantRetriever(_embedding, _store);
    }

    [Fact]
    public async Task Rerank_AsksVectorForCandidatePool_ThenReturnsRerankerTopN()
    {
        var vectorHits = new List<RetrievedChunk> { new("a", 0.5, "kb", 0.5), new("b", 0.4, "kb", 0.4) };
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>(vectorHits));
        IReadOnlyList<RetrievedChunk> reranked = [new("b", 0.95, "kb", 0.4)];
        _reranker.RerankAsync("q", Arg.Any<IReadOnlyList<RetrievedChunk>>(), 8, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(reranked));

        var result = await new QdrantRerankRetriever(Vector(), _reranker, Options).RetrieveAsync("q", 8, CancellationToken.None);

        // vector-কে topN (৮) নয়, Candidates (২০) চাওয়া হয় — reranker-এর সাজানোর জন্য বড় পুল দরকার।
        await _store.Received().SearchAsync(Arg.Any<float[]>(), 20, Arg.Any<CancellationToken>());
        Assert.Equal("b", result[0].Text);
        Assert.Equal(0.95, result[0].Score);
    }

    [Fact]
    public async Task Rerank_WhenRerankerFails_PropagatesSoToolCanFallBack()
    {
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>([new("a", 0.5, "kb", 0.5)]));
        _reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RetrievedChunk>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RetrievedChunk>>(_ => throw new InvalidOperationException("disabled"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new QdrantRerankRetriever(Vector(), _reranker, Options).RetrieveAsync("q", 8, CancellationToken.None));
    }

    [Fact]
    public async Task Hybrid_AddsLexicalOnlyChunk_AndKeepsVectorScoreOfSemanticHits()
    {
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>([new("semantic only chunk", 0.6, "kb", 0.6)]));
        // vector কিছুতেই "Shiplu" ধরে না, কিন্তু শব্দ-খোঁজা ধরে
        _store.ScrollAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>(
            [
                new("semantic only chunk", null, "kb"),
                new("Marketing: Shiplu and Likhon", null, "kb"),
            ]));

        var hybrid = new QdrantHybridRetriever(Vector(), _store, Options);
        var result = await hybrid.RetrieveAsync("Shiplu", 5, CancellationToken.None);

        Assert.Contains(result, c => c.Text == "Marketing: Shiplu and Likhon");
        var semantic = result.Single(c => c.Text == "semantic only chunk");
        Assert.Equal(0.6, semantic.VectorScore); // gate-এর জন্য মূল cosine হারায়নি
    }

    [Fact]
    public async Task HybridRerank_FeedsHybridCandidatesToReranker()
    {
        _store.SearchAsync(Arg.Any<float[]>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>([new("v", 0.5, "kb", 0.5)]));
        _store.ScrollAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>([new("lexical notice chunk", null, "kb")]));
        IReadOnlyList<RetrievedChunk>? seen = null;
        _reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<RetrievedChunk>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => { seen = call.ArgAt<IReadOnlyList<RetrievedChunk>>(1); return Task.FromResult(seen); });

        var hybrid = new QdrantHybridRetriever(Vector(), _store, Options);
        await new QdrantHybridRerankRetriever(hybrid, _reranker, Options).RetrieveAsync("notice", 8, CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Contains(seen!, c => c.Text == "lexical notice chunk");
        Assert.Contains(seen!, c => c.Text == "v");
    }

    [Fact]
    public void Names_MatchPublicRetrievalMethods()
    {
        var vector = Vector();
        var hybrid = new QdrantHybridRetriever(vector, _store, Options);
        Assert.Equal(RetrievalMethods.Vector, vector.Name);
        Assert.Equal(RetrievalMethods.Hybrid, hybrid.Name);
        Assert.Equal(RetrievalMethods.Rerank, new QdrantRerankRetriever(vector, _reranker, Options).Name);
        Assert.Equal(RetrievalMethods.HybridRerank, new QdrantHybridRerankRetriever(hybrid, _reranker, Options).Name);
    }
}
