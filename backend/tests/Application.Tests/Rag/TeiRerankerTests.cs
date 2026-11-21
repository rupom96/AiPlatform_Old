using System.Net;
using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Rag;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Rag;

public class TeiRerankerTests
{
    private static readonly IReadOnlyList<RetrievedChunk> Candidates =
    [
        new("grounding rules chunk", 0.42, "kb.md", VectorScore: 0.42),
        new("resignation: notice is two months", 0.39, "kb.md", VectorScore: 0.39),
        new("office schedule chunk", 0.30, "kb.md", VectorScore: 0.30),
    ];

    [Fact]
    public async Task ReordersByRerankerScore_AndReplacesScore_KeepingVectorScore()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"index":1,"score":0.97},{"index":0,"score":0.12},{"index":2,"score":0.01}]""");

        var result = await Reranker(handler).RerankAsync("নোটিশ পিরিয়ড কত?", Candidates, topN: 2, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("resignation: notice is two months", result[0].Text);
        Assert.Equal(0.97, result[0].Score);
        Assert.Equal(0.39, result[0].VectorScore); // gate-এর জন্য মূল cosine অক্ষত
        Assert.Equal("grounding rules chunk", result[1].Text);
    }

    [Fact]
    public async Task SendsQueryAndAllCandidateTexts()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");

        await Reranker(handler).RerankAsync("q?", Candidates, 3, CancellationToken.None);

        Assert.EndsWith("/rerank", handler.LastUrl);
        Assert.Contains("\"query\":\"q?\"", handler.LastBody);
        Assert.Contains("resignation: notice is two months", handler.LastBody);
        Assert.Contains("\"truncate\":true", handler.LastBody);
    }

    [Fact]
    public async Task Disabled_Throws_SoCallerFallsBackExplicitly()
    {
        var reranker = Reranker(new StubHandler(HttpStatusCode.OK, "[]"), enabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reranker.RerankAsync("q", Candidates, 3, CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_ThrowsProviderException()
    {
        var reranker = Reranker(new StubHandler(HttpStatusCode.InternalServerError, "boom"));

        await Assert.ThrowsAsync<LlmProviderException>(() =>
            reranker.RerankAsync("q", Candidates, 3, CancellationToken.None));
    }

    [Fact]
    public async Task NoCandidates_ReturnsEmpty_WithoutCallingServer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "[]");

        var result = await Reranker(handler).RerankAsync("q", [], 3, CancellationToken.None);

        Assert.Empty(result);
        Assert.Null(handler.LastUrl);
    }

    [Fact]
    public async Task OutOfRangeIndex_IsIgnored()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """[{"index":99,"score":0.9},{"index":0,"score":0.5}]""");

        var result = await Reranker(handler).RerankAsync("q", Candidates, 3, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("grounding rules chunk", result[0].Text);
    }

    private static TeiReranker Reranker(HttpMessageHandler handler, bool enabled = true)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return new TeiReranker(factory, Options.Create(new RerankerSettings { Enabled = enabled, BaseUrl = "http://tei:80", TimeoutSeconds = 5 }));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri!.ToString();
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
