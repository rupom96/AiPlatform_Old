using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Agent;
using AiPlatform.Infrastructure.Agent;
using NSubstitute;

namespace AiPlatform.Application.Tests.Agent;

public class RunAgentCommandHandlerTests
{
    private readonly IChatProviderResolver _resolver = Substitute.For<IChatProviderResolver>();
    private readonly ILlmUsageRecorder _usage = Substitute.For<ILlmUsageRecorder>();
    private readonly ITool[] _tools = [new CurrentTimeTool()];
    private readonly IToolCatalog _catalog = PassthroughCatalog();
    private readonly IAgentRetrievalTrace _trace = EmptyTrace();

    private RunAgentCommandHandler Handler(AgentSettings? settings = null) =>
        new(_resolver, _tools, _catalog, _trace, _usage,
            settings ?? new AgentSettings { Enabled = true, MaxIterations = 5 });

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

        var handler = new RunAgentCommandHandler(
            _resolver, [sales, stock], catalog, _trace, _usage,
            new AgentSettings { Enabled = true, MaxIterations = 5 });
        var result = await handler.Handle(new RunAgentCommand("বিক্রি কত?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(seen);
        Assert.Single(seen);
        Assert.Equal("get_sales", seen[0].Name);
        Assert.NotNull(result.Value.ToolHits);
        Assert.Equal("get_sales", result.Value.ToolHits![0].Label);
    }
}
