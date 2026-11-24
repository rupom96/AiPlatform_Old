using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Features.Agent;
using AiPlatform.Infrastructure.Agent;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// user-এর বাছা retrieval পদ্ধতি: tool সেটা আগে চেষ্টা করে; ব্যর্থ হলে আগের পথে ফেরে এবং hit-এর লেবেলে আসল পদ্ধতির নাম থাকে।
public class RetrievalMethodTests
{
    private readonly IRetrieverResolver _resolver = Substitute.For<IRetrieverResolver>();
    private readonly AgentRetrievalTrace _trace = new();

    private KnowledgeBaseSearchTool Tool() => new(_resolver, _trace);

    private static JsonElement Args(string query) => JsonDocument.Parse($$"""{"query":"{{query}}"}""").RootElement;

    // NSubstitute: .Returns(...) ভেতরে আরেকটা substitute বানালে "last call" গুলিয়ে যায় — তাই আগে বানিয়ে পরে বসাই।
    private void Register(string method, IRetriever retriever) => _resolver.Resolve(method).Returns(retriever);

    private static IRetriever Retriever(string name, params RetrievedChunk[] chunks)
    {
        var retriever = Substitute.For<IRetriever>();
        retriever.Name.Returns(name);
        retriever.RetrieveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RetrievedChunk>>(chunks));
        return retriever;
    }

    private static IRetriever Failing(string name)
    {
        var retriever = Substitute.For<IRetriever>();
        retriever.Name.Returns(name);
        retriever.RetrieveAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RetrievedChunk>>(_ => throw new InvalidOperationException("reranker disabled"));
        return retriever;
    }

    [Fact]
    public async Task RequestedMethod_IsTriedFirst_AndLabelsTheHits()
    {
        Register("qdrant-rerank", Retriever("qdrant-rerank", new RetrievedChunk("reranked", 0.97, "kb.md", 0.4)));
        Register("qdrant", Retriever("qdrant", new RetrievedChunk("plain", 0.4, "kb.md", 0.4)));
        _trace.Begin("qdrant-rerank");

        var output = await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Contains("reranked", output);
        Assert.DoesNotContain("plain", output);
        Assert.StartsWith("qdrant-rerank:", _trace.DocumentHits[0].Detail);
    }

    // reranker বন্ধ/ব্যর্থ → নীরবে ভুল ক্রম নয়: আগের পথে ফেরে, আর লেবেলে "qdrant" দেখায় (তুলনা মিথ্যা হয় না)।
    [Fact]
    public async Task RequestedMethodFails_FallsBackToVector_AndLabelShowsActualMethod()
    {
        Register("qdrant-rerank", Failing("qdrant-rerank"));
        Register("qdrant", Retriever("qdrant", new RetrievedChunk("plain", 0.4, "kb.md", 0.4)));
        _trace.Begin("qdrant-rerank");

        var output = await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Contains("plain", output);
        Assert.StartsWith("qdrant:", _trace.DocumentHits[0].Detail);
    }

    // UI এটা দেখেই জানায় বাছা পদ্ধতি সত্যিই চলেছে কিনা (ফল্লব্যাকে আসল নাম)।
    [Fact]
    public async Task MethodUsed_IsTheMethodThatActuallyAnswered()
    {
        Register("qdrant-rerank", Failing("qdrant-rerank"));
        Register("qdrant", Retriever("qdrant", new RetrievedChunk("plain", 0.4, "kb.md", 0.4)));
        _trace.Begin("qdrant-rerank");

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Equal("qdrant", _trace.MethodUsed); // চাওয়া হয়েছিল qdrant-rerank
    }

    [Fact]
    public async Task MethodUsed_IsNull_WhenNothingFound()
    {
        Register("qdrant", Retriever("qdrant"));
        Register("hybrid", Retriever("hybrid"));
        Register("keyword", Retriever("keyword"));
        _trace.Begin(null);

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Null(_trace.MethodUsed);
    }

    [Fact]
    public async Task NoRequestedMethod_UsesDefaultVectorPath_AsBefore()
    {
        Register("qdrant", Retriever("qdrant", new RetrievedChunk("plain", 0.4, "kb.md", 0.4)));
        _trace.Begin(null);

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        _resolver.DidNotReceive().Resolve("qdrant-rerank");
        _resolver.DidNotReceive().Resolve("qdrant-hybrid");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("qdrant")]
    [InlineData("")]
    public async Task DefaultAliases_DoNotTriggerAnExtraMethod(string requested)
    {
        Register("qdrant", Retriever("qdrant", new RetrievedChunk("plain", 0.4, "kb.md", 0.4)));
        _trace.Begin(requested);

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        _resolver.DidNotReceive().Resolve("default");
    }

    // gate-এর জন্য মূল vector cosine (rerank/RRF Score নয়) trace-এ যায়।
    [Fact]
    public async Task BestVectorScore_IsTheOriginalCosine_NotTheRerankScore()
    {
        Register("qdrant-rerank", Retriever("qdrant-rerank",
            new RetrievedChunk("a", 0.99, "kb.md", 0.41),
            new RetrievedChunk("b", 0.50, "kb.md", 0.58)));
        _trace.Begin("qdrant-rerank");

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Equal(0.58, _trace.BestVectorScore);
    }

    // reranker score শুধু rerank পদ্ধতিতে gate-সংকেত; অন্য পদ্ধতিতে Score অন্য মাপের (cosine/RRF) তাই null।
    [Theory]
    [InlineData("qdrant-rerank", true)]
    [InlineData("qdrant-hybrid-rerank", true)]
    [InlineData("qdrant-hybrid", false)]
    [InlineData("qdrant", false)]
    public async Task BestRerankScore_IsSetOnlyForRerankMethods(string method, bool expectScore)
    {
        Register(method, Retriever(method, new RetrievedChunk("a", 0.46, "kb.md", 0.41), new RetrievedChunk("b", 0.01, "kb.md", 0.30)));
        _trace.Begin(method == "qdrant" ? null : method);

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        if (expectScore) Assert.Equal(0.46, _trace.BestRerankScore);
        else Assert.Null(_trace.BestRerankScore);
    }

    [Fact]
    public async Task NoVectorScoreInHits_LeavesBestVectorScoreNull()
    {
        Register("qdrant", Retriever("qdrant")); // খালি
        Register("hybrid", Retriever("hybrid"));
        Register("keyword", Retriever("keyword", new RetrievedChunk("seed", 2, "knowledge-base")));
        _trace.Begin(null);

        await Tool().ExecuteAsync(Args("q"), CancellationToken.None);

        Assert.Null(_trace.BestVectorScore);
    }

    [Fact]
    public async Task Trace_RequestedMethod_IsVisibleToChildAsyncMethods_AndIsolatedPerBegin()
    {
        _trace.Begin("qdrant-hybrid");
        var seenInChild = await ReadInChildAsync();
        Assert.Equal("qdrant-hybrid", seenInChild);

        _trace.Begin(null);
        Assert.Null(_trace.RequestedMethod);
    }

    private async Task<string?> ReadInChildAsync()
    {
        await Task.Yield();
        return _trace.RequestedMethod;
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("default", true)]
    [InlineData("qdrant", true)]
    [InlineData("qdrant-hybrid", true)]
    [InlineData("qdrant-rerank", true)]
    [InlineData("qdrant-hybrid-rerank", true)]
    [InlineData("QDRANT-RERANK", true)]
    [InlineData("rerankk", false)]
    [InlineData("hybrid", false)] // পুরোনো seed-only "hybrid" এই API-তে বাছা যায় না
    public void RetrievalMethods_IsValid(string? method, bool expected) =>
        Assert.Equal(expected, RetrievalMethods.IsValid(method));

    [Fact]
    public void Validator_RejectsUnknownMethod_AndAcceptsKnown()
    {
        var validator = new RunAgentCommandValidator();

        Assert.False(validator.Validate(new RunAgentCommand("hi", "groq", RetrievalMethod: "bogus")).IsValid);
        Assert.True(validator.Validate(new RunAgentCommand("hi", "groq", RetrievalMethod: "qdrant-rerank")).IsValid);
        Assert.True(validator.Validate(new RunAgentCommand("hi", "groq")).IsValid);
    }
}
