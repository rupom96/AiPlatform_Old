using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AiPlatform.Application.Tests.Llm;

// Temperature ও ReasoningEffort শুধু config-এ থাকলেই request body-তে যায়; model-picker-এ model বদলালেও (override)
// provider-এর Temperature হারায় না। gemma4:e2b-র মতো model-এ default 1.0 এলোমেলো হয়, তাই এটা পাহারা দেওয়া দরকার।
public class OpenAiCompatibleChatProviderTests
{
    private const string OkResponse = """{"choices":[{"message":{"content":"ok"}}]}""";

    [Fact]
    public async Task ConfiguredTemperature_IsSentInRequestBody()
    {
        var handler = new CapturingHandler();
        var provider = new OpenAiCompatibleChatProvider(
            FactoryFor(handler), "ollama",
            new ProviderConfig { BaseUrl = "http://localhost:11434/v1/chat/completions", Model = "m", RequiresApiKey = false, Temperature = 0.2 });

        await provider.CompleteAsync([new ChatMessage("user", "hi")], CancellationToken.None);

        Assert.Contains("\"temperature\":0.2", handler.LastBody);
    }

    [Fact]
    public async Task NoTemperatureConfigured_SendsNone_SoServerDefaultApplies()
    {
        var handler = new CapturingHandler();
        var provider = new OpenAiCompatibleChatProvider(
            FactoryFor(handler), "groq",
            new ProviderConfig { BaseUrl = "https://example.test/v1/chat/completions", Model = "m", RequiresApiKey = false });

        await provider.CompleteAsync([new ChatMessage("user", "hi")], CancellationToken.None);

        Assert.DoesNotContain("temperature", handler.LastBody);
        Assert.DoesNotContain("reasoning_effort", handler.LastBody);
    }

    // UI-তে ollama + gemma4:e2b বাছলে resolver config clone করে নতুন provider বানায় — Temperature/ReasoningEffort যেন টিকে থাকে।
    [Fact]
    public async Task ModelOverride_KeepsTemperatureAndReasoningEffort()
    {
        var handler = new CapturingHandler();
        var settings = new LlmSettings
        {
            Providers = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollama"] = new ProviderConfig
                {
                    BaseUrl = "http://localhost:11434/v1/chat/completions",
                    Model = "llama3.1:8b",
                    RequiresApiKey = false,
                    Temperature = 0.2,
                    ReasoningEffort = "none"
                }
            }
        };
        var resolver = new ChatProviderResolver(
            FactoryFor(handler), Options.Create(settings), Options.Create(new FallbackSettings()),
            NullLogger<FallbackChatProvider>.Instance);

        var provider = resolver.Resolve("ollama", "gemma4:e2b");
        await provider!.CompleteAsync([new ChatMessage("user", "hi")], CancellationToken.None);

        Assert.Contains("\"model\":\"gemma4:e2b\"", handler.LastBody);
        Assert.Contains("\"temperature\":0.2", handler.LastBody);
        Assert.Contains("\"reasoning_effort\":\"none\"", handler.LastBody);
    }

    private static IHttpClientFactory FactoryFor(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return factory;
    }

    // নেটওয়ার্ক ছাড়া উত্তর বানায় এবং শেষ request body মনে রাখে।
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
