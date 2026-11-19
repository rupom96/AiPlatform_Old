using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Agent;
using AiPlatform.Infrastructure.Agent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AiPlatform.Application.Tests.Agent;

public class RunAgentCommandHandlerTests
{
    private readonly IChatProviderResolver _resolver = Substitute.For<IChatProviderResolver>();
    private readonly ILlmUsageRecorder _usage = Substitute.For<ILlmUsageRecorder>();
    private readonly ITool[] _tools = [new CurrentTimeTool()];
    private readonly IToolCatalog _catalog = PassthroughCatalog();
    private readonly IAgentRetrievalTrace _trace = EmptyTrace();

    // বহু-প্রশ্ন ভাঙার পথ এই handler-এর অন্য কেউ (MultiQuestionRunnerTests) পরীক্ষা করে; এখানে runner নিষ্ক্রিয় scope-factory-র।
    private RunAgentCommandHandler Handler(AgentSettings? settings = null)
    {
        var effective = settings ?? new AgentSettings { Enabled = true, MaxIterations = 5 };
        return new(_resolver, _tools, _catalog, _trace, _usage, effective,
            new MultiQuestionRunner(Substitute.For<IServiceScopeFactory>(), effective));
    }

    private static IToolCatalog PassthroughCatalog()
    {
        var catalog = Substitute.For<IToolCatalog>();
        catalog.SyncAsync(Arg.Any<IReadOnlyCollection<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        catalog.SelectForPromptAsync(
                Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(callInfo.ArgAt<IReadOnlyList<ITool>>(0)));
        catalog.SearchScoresAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredHit>>([]));
        return catalog;
    }

    private static IAgentRetrievalTrace EmptyTrace()
    {
        var trace = Substitute.For<IAgentRetrievalTrace>();
        trace.DocumentHits.Returns(Array.Empty<ScoredHit>());
        return trace;
    }

    private static IChatProvider ToolProvider(out IToolCallingProvider tool)
    {
        var provider = Substitute.For<IChatProvider, IToolCallingProvider>();
        tool = (IToolCallingProvider)provider;
        return provider;
    }

    [Fact]
    public async Task CallsTool_ThenReturnsFinalAnswer()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(
                new ChatToolResult(null, [new ToolCall("c1", "current_time", "{}")], "groq", "llama"),
                new ChatToolResult("এখন সময় জানানো হলো।", [], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);

        var result = await Handler().Handle(new RunAgentCommand("কয়টা বাজে?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("এখন সময় জানানো হলো।", result.Value.Answer);
        Assert.Contains("current_time", result.Value.ToolsUsed);
        await _catalog.Received().SyncAsync(Arg.Any<IReadOnlyCollection<ITool>>(), Arg.Any<CancellationToken>());
    }

    // এক বার্তায় একাধিক প্রশ্ন (দুই লাইন) → প্রতিটা আলাদা run; মূল handler নিজে LLM ডাকে না।
    [Fact]
    public async Task MultiLineMessage_IsSplit_AndEachQuestionRunsSeparately()
    {
        var provider = ToolProvider(out var tool);
        _resolver.Resolve("groq", null).Returns(provider);

        var inner = Substitute.For<IRequestHandler<RunAgentCommand, Result<AgentResponse>>>();
        inner.Handle(Arg.Any<RunAgentCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<Result<AgentResponse>>(
                new AgentResponse($"উত্তর: {call.Arg<RunAgentCommand>().Message}", [], 1)));
        var handler = HandlerWithInner(inner, new AgentSettings { Enabled = true });

        var result = await handler.Handle(
            new RunAgentCommand("Who is Rashed Kamal?\nJuly 2026 er total sales koto?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("1. Who is Rashed Kamal?\nউত্তর: Who is Rashed Kamal?", result.Value.Answer);
        Assert.Contains("2. July 2026 er total sales koto?\nউত্তর: July 2026 er total sales koto?", result.Value.Answer);
        await inner.Received(2).Handle(Arg.Is<RunAgentCommand>(c => c.SkipSplit), Arg.Any<CancellationToken>());
        await tool.DidNotReceive().CompleteWithToolsAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SplitDisabled_RunsTheWholeMessageOnce()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("এক উত্তর", [], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);
        var inner = Substitute.For<IRequestHandler<RunAgentCommand, Result<AgentResponse>>>();
        var handler = HandlerWithInner(inner, new AgentSettings { Enabled = true, SplitQuestions = false });

        var result = await handler.Handle(new RunAgentCommand("এক?\nদুই?", "groq"), CancellationToken.None);

        Assert.Equal("এক উত্তর", result.Value.Answer);
        await inner.DidNotReceive().Handle(Arg.Any<RunAgentCommand>(), Arg.Any<CancellationToken>());
    }

    // ভাঙা প্রশ্ন (SkipSplit) আবার ভাঙে না — নইলে অনন্ত পুনরাবৃত্তি
    [Fact]
    public async Task SkipSplit_NeverSplitsAgain()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("ঠিক", [], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);
        var inner = Substitute.For<IRequestHandler<RunAgentCommand, Result<AgentResponse>>>();
        var handler = HandlerWithInner(inner, new AgentSettings { Enabled = true });

        var result = await handler.Handle(new RunAgentCommand("এক?\nদুই?", "groq", SkipSplit: true), CancellationToken.None);

        Assert.Equal("ঠিক", result.Value.Answer);
        await inner.DidNotReceive().Handle(Arg.Any<RunAgentCommand>(), Arg.Any<CancellationToken>());
    }

    private RunAgentCommandHandler HandlerWithInner(
        IRequestHandler<RunAgentCommand, Result<AgentResponse>> inner, AgentSettings settings)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => inner);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new(_resolver, _tools, _catalog, _trace, _usage, settings, new MultiQuestionRunner(scopeFactory, settings));
    }

    [Fact]
    public async Task NoToolCalls_ReturnsAnswerImmediately()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("সরাসরি উত্তর", [], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);

        var result = await Handler().Handle(new RunAgentCommand("hi", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.ToolsUsed);
        // UI সময়ের পাশে দেখায় কোন provider/model আসলে উত্তর দিল (fallback হলে অনুরোধের নাম থেকে আলাদা হতে পারে)
        Assert.Equal("groq", result.Value.Provider);
        Assert.Equal("llama", result.Value.Model);
    }

    [Fact]
    public async Task ExceedsMaxIterations_ReturnsFailure()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult(null, [new ToolCall("c1", "current_time", "{}")], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);

        var result = await Handler(new AgentSettings { Enabled = true, MaxIterations = 2 })
            .Handle(new RunAgentCommand("loop", "groq"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Agent.MaxIterations", result.Error.Code);
    }

    [Fact]
    public async Task ProviderWithoutToolSupport_ReturnsValidationError()
    {
        _resolver.Resolve("gemini", null).Returns(Substitute.For<IChatProvider>());

        var result = await Handler().Handle(new RunAgentCommand("hi", "gemini"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task Disabled_ReturnsValidationError()
    {
        var result = await Handler(new AgentSettings { Enabled = false })
            .Handle(new RunAgentCommand("hi", "groq"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Agent.Disabled", result.Error.Code);
    }

    [Fact]
    public async Task PassesModelOverride_ToResolver()
    {
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("ok", [], "ollama-server", "qwen3.5:4b"));
        _resolver.Resolve("ollama-server", "qwen3.5:4b").Returns(provider);

        var result = await Handler().Handle(
            new RunAgentCommand("hi", "ollama-server", "qwen3.5:4b"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _resolver.Received().Resolve("ollama-server", "qwen3.5:4b");
    }

    [Fact]
    public async Task UsesRetrievedToolSubset_ForLlmMenu()
    {
        var sales = Substitute.For<ITool>();
        sales.Name.Returns("get_sales");
        sales.Description.Returns("sales");
        sales.ParametersSchema.Returns(new { type = "object", properties = new { } });

        var stock = Substitute.For<ITool>();
        stock.Name.Returns("get_stock");
        stock.Description.Returns("stock");
        stock.ParametersSchema.Returns(new { type = "object", properties = new { } });

        var catalog = Substitute.For<IToolCatalog>();
        catalog.SyncAsync(Arg.Any<IReadOnlyCollection<ITool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        catalog.SelectForPromptAsync(Arg.Any<IReadOnlyList<ITool>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITool>>([sales]));
        catalog.SearchScoresAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredHit>>([new ScoredHit("get_sales", 0.91)]));

        IReadOnlyList<ToolDefinition>? seen = null;
        var provider = ToolProvider(out var tool);
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                seen = callInfo.ArgAt<IReadOnlyList<ToolDefinition>>(1);
                return new ChatToolResult("ok", [], "groq", "llama");
            });
        _resolver.Resolve("groq", null).Returns(provider);

        var settings = new AgentSettings { Enabled = true, MaxIterations = 5 };
        var handler = new RunAgentCommandHandler(
            _resolver, [sales, stock], catalog, _trace, _usage, settings,
            new MultiQuestionRunner(Substitute.For<IServiceScopeFactory>(), settings));
        var result = await handler.Handle(new RunAgentCommand("বিক্রি কত?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(seen);
        Assert.Single(seen);
        Assert.Equal("get_sales", seen[0].Name);
        Assert.NotNull(result.Value.ToolHits);
        Assert.Equal("get_sales", result.Value.ToolHits![0].Label);
    }
}
