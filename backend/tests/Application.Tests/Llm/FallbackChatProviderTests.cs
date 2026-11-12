using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace AiPlatform.Application.Tests.Llm;

// fallback ভুল হলে request নীরবে মরবে বা ভুল provider-এ যাবে — দুটোই ধরা কঠিন production-এ।
public class FallbackChatProviderTests
{
    private static readonly ChatMessage[] Messages = [new("user", "hi")];
    private static readonly ToolDefinition[] Tools = [new("t", "d", new { })];

    [Fact]
    public async Task FirstProviderSucceeds_SecondNeverCalled()
    {
        var first = Working("cerebras");
        var second = Working("groq");

        var result = await Chain(first, second).CompleteAsync(Messages, CancellationToken.None);

        Assert.Equal("cerebras", result.Provider);
        await second.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FirstProviderQuotaExhausted_FallsBackToSecond()
    {
        var first = Failing("cerebras", new LlmProviderException("cerebras", 429, "rate limit"));
        var second = Working("groq");

        var result = await Chain(first, second).CompleteAsync(Messages, CancellationToken.None);

        Assert.Equal("groq", result.Provider);
    }

    [Fact]
    public async Task TransientNetworkFailure_FallsBack()
    {
        var first = Failing("cerebras", new HttpRequestException("connection reset"));
        var result = await Chain(first, Working("groq")).CompleteAsync(Messages, CancellationToken.None);

        Assert.Equal("groq", result.Provider);
    }

    [Fact]
    public async Task Timeout_FallsBack()
    {
        // HttpClient timeout TaskCanceledException হিসেবে আসে।
        var first = Failing("ollama", new TaskCanceledException("timeout"));
        var result = await Chain(first, Working("groq")).CompleteAsync(Messages, CancellationToken.None);

        Assert.Equal("groq", result.Provider);
    }

    // আমাদের নিজেদের bug সব provider-এই ঘটবে — fallback করে শুধু দেরি হয়, তাই সঙ্গে সঙ্গে throw।
    [Fact]
    public async Task NonRetryableException_DoesNotFallBack()
    {
        var first = Failing("cerebras", new InvalidOperationException("bug in our code"));
        var second = Working("groq");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Chain(first, second).CompleteAsync(Messages, CancellationToken.None));

        await second.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    // শেষ provider-ও ব্যর্থ → আসল error উপরে যাক, আমাদের বানানো generic error নয়।
    [Fact]
    public async Task AllProvidersFail_ThrowsLastRealError()
    {
        var chain = Chain(
            Failing("cerebras", new LlmProviderException("cerebras", 429, "quota")),
            Failing("groq", new LlmProviderException("groq", 503, "overloaded")));

        var error = await Assert.ThrowsAsync<LlmProviderException>(
            () => chain.CompleteAsync(Messages, CancellationToken.None));

        Assert.Equal("groq", error.Provider);
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task ToolCalling_FallsBackToNextToolCapableProvider()
    {
        var first = FailingToolProvider("cerebras", new LlmProviderException("cerebras", 429, "quota"));
        var second = WorkingToolProvider("groq");

        var result = await Chain(first, second).CompleteWithToolsAsync(Messages, Tools, CancellationToken.None);

        Assert.Equal("groq", result.Provider);
    }

    // gemini tool-calling পারে না — chain-এ থাকলেও tool request-এ তাকে এড়িয়ে যেতে হবে।
    [Fact]
    public async Task ToolCalling_SkipsProvidersWithoutToolSupport()
    {
        var chatOnly = Working("gemini");                 // IToolCallingProvider নয়
        var toolCapable = WorkingToolProvider("groq");

        var result = await Chain(chatOnly, toolCapable)
            .CompleteWithToolsAsync(Messages, Tools, CancellationToken.None);

        Assert.Equal("groq", result.Provider);
    }

    [Fact]
    public async Task ToolCalling_WithNoCapableProvider_Throws()
    {
        var chain = Chain(Working("gemini"), Working("gemini2"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => chain.CompleteWithToolsAsync(Messages, Tools, CancellationToken.None));
    }

    // --- helpers ---

    private static FallbackChatProvider Chain(params IChatProvider[] providers)
        => new(providers, NullLogger<FallbackChatProvider>.Instance);

    private static IChatProvider Working(string name)
    {
        var provider = Substitute.For<IChatProvider>();
        provider.Name.Returns(name);
        provider.CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResult("ok", name, "model"));
        return provider;
    }

    private static IChatProvider Failing(string name, Exception error)
    {
        var provider = Substitute.For<IChatProvider>();
        provider.Name.Returns(name);
        provider.CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);
        return provider;
    }

    private static IChatProvider WorkingToolProvider(string name)
    {
        var provider = Substitute.For<IChatProvider, IToolCallingProvider>();
        provider.Name.Returns(name);
        ((IToolCallingProvider)provider).CompleteWithToolsAsync(
                Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("ok", [], name, "model"));
        return provider;
    }

    private static IChatProvider FailingToolProvider(string name, Exception error)
    {
        var provider = Substitute.For<IChatProvider, IToolCallingProvider>();
        provider.Name.Returns(name);
        ((IToolCallingProvider)provider).CompleteWithToolsAsync(
                Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(error);
        return provider;
    }
}
