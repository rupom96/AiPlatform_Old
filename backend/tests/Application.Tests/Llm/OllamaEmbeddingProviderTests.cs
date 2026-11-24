using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Llm;

// QueryTask থাকলে শুধু *খোঁজার প্রশ্নে* Qwen3 ছাঁচ বসে; document-এ বসে না (asymmetric retrieval)।
// না থাকলে আগের মতোই সরাসরি লেখা যায় (nomic-embed-text)।
public class OllamaEmbeddingProviderTests
{
    private const string OkResponse = """{"embeddings":[[0.1,0.2,0.3]]}""";

    [Fact]
    public async Task Query_WithQueryTask_IsWrappedInInstructFormat()
    {
        var handler = new CapturingHandler();
        var provider = ProviderFor(handler, queryTask: "find answers");

        await provider.EmbedAsync("office hours", EmbeddingTaskType.RetrievalQuery, CancellationToken.None);

        // নতুন লাইন JSON-এ "\n" হয়ে যায়, তাই body-তে ব্যাকস্ল্যাশ-n খুঁজি।
        Assert.Contains("\"input\":\"Instruct: find answers\\nQuery: office hours\"", handler.LastBody);
    }

    [Fact]
    public async Task Document_WithQueryTask_IsSentUnchanged()
    {
        var handler = new CapturingHandler();
        var provider = ProviderFor(handler, queryTask: "find answers");

        await provider.EmbedAsync("office hours 10-7", EmbeddingTaskType.RetrievalDocument, CancellationToken.None);

        Assert.Contains("\"input\":\"office hours 10-7\"", handler.LastBody);
        Assert.DoesNotContain("Instruct", handler.LastBody);
    }

    [Fact]
    public async Task Query_WithoutQueryTask_IsSentUnchanged()
    {
        var handler = new CapturingHandler();
        var provider = ProviderFor(handler, queryTask: null);

        await provider.EmbedAsync("office hours", EmbeddingTaskType.RetrievalQuery, CancellationToken.None);

        Assert.Contains("\"input\":\"office hours\"", handler.LastBody);
    }

    private static OllamaEmbeddingProvider ProviderFor(HttpMessageHandler handler, string? queryTask)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        var settings = new LlmSettings
        {
            Embedding = new EmbeddingConfig { Provider = "ollama", Model = "m", BaseUrl = "http://localhost:11434", QueryTask = queryTask }
        };
        return new OllamaEmbeddingProvider(factory, Options.Create(settings));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(OkResponse, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
