using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Llm;

// 2026-11-11: UI-তে ollama-র Local/Server toggle এলো, তাই provider-এর নাম এখন caller পাঠায়।
// নামটা configure করা key হিসেবেই খোঁজা হয় — URL হিসেবে নয়। এই টেস্ট সেটাই পাহারা দেয়:
// অচেনা নামে কোনো HTTP call-ই হওয়া উচিত নয় (নইলে ইচ্ছেমতো host ডাকানো যেত)।
public class OllamaModelCatalogTests
{
    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("http://169.254.169.254")]
    [InlineData("groq")]            // configure করা, কিন্তু এই test-এ নেই
    public async Task UnknownProvider_ReturnsEmpty_WithoutCallingAnything(string provider)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var catalog = Build(factory);

        var models = await catalog.ListModelsAsync(provider, CancellationToken.None);

        Assert.Empty(models);
        // HttpClient চাওয়াই হয়নি = নেটওয়ার্কে কিছু যায়নি
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    // BaseUrl খালি থাকলেও একই — UI তখন "পৌঁছানো যাচ্ছে না" দেখায়, crash নয়।
    [Fact]
    public async Task ConfiguredProviderWithoutBaseUrl_ReturnsEmpty()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var catalog = Build(factory, new ProviderConfig { Model = "x", RequiresApiKey = false });

        var models = await catalog.ListModelsAsync("ollama", CancellationToken.None);

        Assert.Empty(models);
        factory.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    // vLLM: Engine = "vllm" হলে তালিকা আসে OpenAI-style GET /v1/models থেকে (data[].id), /api/tags থেকে নয়।
    [Fact]
    public async Task VllmEngine_ListsModelIds_FromV1Models()
    {
        var handler = new StubHandler(
            """{"object":"list","data":[{"id":"dbz-ai","object":"model"},{"id":"other","object":"model"}]}""");
        var catalog = BuildVllm(handler);

        var models = await catalog.ListModelsAsync("vllm", CancellationToken.None);

        Assert.Equal(["dbz-ai", "other"], models);
        Assert.Equal("http://localhost:8000/v1/models", handler.RequestedUrls.Single());
    }

    [Fact]
    public async Task VllmEngine_WhenServerDown_ReturnsEmpty()
    {
        var catalog = BuildVllm(new StubHandler(throwOnSend: true));

        var models = await catalog.ListModelsAsync("vllm", CancellationToken.None);

        Assert.Empty(models);
    }

    private static OllamaModelCatalog BuildVllm(StubHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var settings = new LlmSettings
        {
            Providers = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["vllm"] = new ProviderConfig
                {
                    Engine = "vllm",
                    BaseUrl = "http://localhost:8000/v1/chat/completions",
                    Model = "dbz-ai",
                    RequiresApiKey = false
                }
            }
        };

        return new OllamaModelCatalog(factory, Options.Create(settings), NullLogger<OllamaModelCatalog>.Instance);
    }

    private static OllamaModelCatalog Build(IHttpClientFactory factory, ProviderConfig? ollama = null)
    {
        var settings = new LlmSettings
        {
            Providers = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = ollama ?? new ProviderConfig
                {
                    BaseUrl = "http://localhost:11434/v1/chat/completions",
                    Model = "llama3.1:8b",
                    RequiresApiKey = false
                }
            }
        };

        return new OllamaModelCatalog(
            factory, Options.Create(settings), NullLogger<OllamaModelCatalog>.Instance);
    }

    // নেটওয়ার্ক ছাড়া HTTP উত্তর বানানোর ছোট stub; কোন URL ডাকা হয়েছে তা মনে রাখে।
    private sealed class StubHandler(string? json = null, bool throwOnSend = false) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            if (throwOnSend)
                throw new HttpRequestException("connection refused");

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(json ?? "{}", System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
