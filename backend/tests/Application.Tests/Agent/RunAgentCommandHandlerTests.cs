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

    // model tool না ডেকে "current_time()" লিখে দিলে সেটা উত্তর নয়: একবার সংশোধন করে আবার চেষ্টা, তারপর আসল tool call।
    [Fact]
    public async Task ToolCallWrittenAsText_IsCorrectedOnce_ThenTheRealToolRuns()
    {
        var provider = ToolProvider(out var tool);
        IReadOnlyList<ChatMessage>? secondCallMessages = null;
        var call = 0;
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                call++;
                if (call == 2) secondCallMessages = info.ArgAt<IReadOnlyList<ChatMessage>>(0).ToList();
                return call switch
                {
                    1 => new ChatToolResult("current_time() - এখনকার সময়", [], "groq", "llama"),
                    2 => new ChatToolResult(null, [new ToolCall("c1", "current_time", "{}")], "groq", "llama"),
                    _ => new ChatToolResult("এখন সময় জানানো হলো।", [], "groq", "llama"),
                };
            });
        _resolver.Resolve("groq", null).Returns(provider);

        var result = await Handler().Handle(new RunAgentCommand("কয়টা বাজে?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("এখন সময় জানানো হলো।", result.Value.Answer);
        Assert.Contains("current_time", result.Value.ToolsUsed);
        Assert.Contains(secondCallMessages!, m => m.Role == "user" && m.Content.Contains("tool call as plain text"));
    }

    // সংশোধন একবারই — model আবার লিখলে অনন্ত চক্র নয়, দ্বিতীয়বারের উত্তরই যায়।
    [Fact]
    public async Task ToolCallWrittenAsText_TwiceInARow_IsNotRetriedAgain()
    {
        var provider = ToolProvider(out var tool);
        var calls = 0;
        tool.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; return new ChatToolResult("current_time() আবার", [], "groq", "llama"); });
        _resolver.Resolve("groq", null).Returns(provider);

        var result = await Handler().Handle(new RunAgentCommand("কয়টা বাজে?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, calls);
        Assert.Equal("current_time() আবার", result.Value.Answer);
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

    // retrieval-first: LLM চলার আগেই ডকুমেন্ট-খোঁজা চলে এবং passage system prompt-এ ঢোকে (মডেলকে "খুঁজব কিনা" ভাবতে হয় না)।
    [Fact]
    public async Task PreRetrieval_InjectsPassagesIntoSystemPrompt_BeforeLlmRuns()
    {
        var (handler, kb, seenMessages) = KnowledgeBaseHandler(hits: [new ScoredHit("Office Schedule", 0.9)], passages: "[1] office hours 10-7");

        var result = await handler.Handle(new RunAgentCommand("অফিসের সময় কখন?", "groq"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await kb.Received(1).ExecuteAsync(Arg.Any<System.Text.Json.JsonElement>(), Arg.Any<CancellationToken>());
        var user = seenMessages().Single(m => m.Role == "user");
        Assert.Contains("[1] office hours 10-7", user.Content);
        Assert.EndsWith("Question: অফিসের সময় কখন?", user.Content); // passage প্রশ্নের ঠিক আগে
        Assert.Contains("call the matching ERP tool", user.Content);
        Assert.NotNull(result.Value.DocumentHits);
        Assert.DoesNotContain("search_knowledge_base", result.Value.ToolsUsed); // মডেল ডাকেনি, তাই "tool চলেছে" নয়
    }

    // response-এ আসল পদ্ধতির নাম যায় (শুধু ডকুমেন্ট খোঁজা হলে); ERP-only প্রশ্নে null।
    [Fact]
    public async Task Response_CarriesRetrievalMethodUsed_OnlyWhenDocumentsWereSearched()
    {
        var (docHandler, _, _) = KnowledgeBaseHandler(
            hits: [new ScoredHit("x", 0.9)], passages: "[1] p", methodUsed: "qdrant");
        var (erpHandler, _, _) = KnowledgeBaseHandler(hits: [], passages: "none", methodUsed: null);

        var withDocs = await docHandler.Handle(new RunAgentCommand("প্রশ্ন", "groq", RetrievalMethod: "qdrant-rerank"), CancellationToken.None);
        var noDocs = await erpHandler.Handle(new RunAgentCommand("বিক্রি কত?", "groq"), CancellationToken.None);

        Assert.Equal("qdrant", withDocs.Value.RetrievalMethodUsed); // rerank চাওয়া হয়েছিল, চলেছে qdrant
        Assert.Null(noDocs.Value.RetrievalMethodUsed);
    }

    [Fact]
    public async Task PreRetrieval_NoHits_LeavesSystemPromptWithoutPassages()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(hits: [], passages: "No relevant information found.");

        await handler.Handle(new RunAgentCommand("কিছু একটা", "groq"), CancellationToken.None);

        var user = seenMessages().Single(m => m.Role == "user");
        Assert.DoesNotContain("No relevant information found.", user.Content);
        Assert.Equal("কিছু একটা", user.Content); // কিছু না পেলে প্রশ্ন অপরিবর্তিত
    }

    // ERP প্রশ্ন ডকুমেন্টের সাথে কম মেলে — সেরা hit সীমার নিচে হলে passage ঢোকে না, মডেল আগের পথে (tool) যায়।
    [Fact]
    public async Task PreRetrieval_BestHitBelowMinScore_DoesNotInjectPassages()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("weak", 0.30)], passages: "[1] unrelated chunk",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45 }, bestVectorScore: 0.30);

        var result = await handler.Handle(new RunAgentCommand("জুলাইয়ের বিক্রি কত?", "groq"), CancellationToken.None);

        var user = seenMessages().Single(m => m.Role == "user");
        Assert.Equal("জুলাইয়ের বিক্রি কত?", user.Content);
        Assert.Null(result.Value.DocumentHits);
    }

    [Fact]
    public async Task PreRetrieval_BestHitAtOrAboveMinScore_InjectsPassages()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("strong", 0.60)], passages: "[1] relevant chunk",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45 }, bestVectorScore: 0.60);

        await handler.Handle(new RunAgentCommand("অফিসের সময় কখন?", "groq"), CancellationToken.None);

        Assert.Contains("[1] relevant chunk", seenMessages().Single(m => m.Role == "user").Content);
    }

    // hybrid/rerank-এ hits[0].Score অন্য মাপের (RRF ~০.০৩ বা reranker ০–১) — gate তাই মূল vector cosine দেখে, Score নয়।
    [Fact]
    public async Task PreRetrieval_Gate_UsesVectorScore_NotTheRerankedHitScore()
    {
        // Score = 0.02 (RRF-এর মতো ছোট) কিন্তু মূল cosine ০.৬০ → passage ঢুকতে হবে।
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("fused", 0.02)], passages: "[1] relevant chunk",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45 }, bestVectorScore: 0.60);

        await handler.Handle(new RunAgentCommand("অফিসের সময় কখন?", "groq", RetrievalMethod: "qdrant-hybrid"), CancellationToken.None);

        Assert.Contains("[1] relevant chunk", seenMessages().Single(m => m.Role == "user").Content);
    }

    // Rerank চললে gate reranker score দেখে: vector cosine কম (০.৪১ < ০.৪৫) হলেও reranker ০.৪৬ বললে passage ঢোকে
    // ("সাপ্তাহিক ছুটি কোন কোন দিন?" — vector gate ভুল করে আটকাত)।
    [Fact]
    public async Task PreRetrieval_WithRerank_GateUsesRerankScore_NotTheVectorCosine()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("Office Schedule", 0.46)], passages: "[1] weekly holidays Friday",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45, PreRetrieveMinRerankScore = 0.005 },
            bestVectorScore: 0.41, bestRerankScore: 0.46);

        await handler.Handle(new RunAgentCommand("সাপ্তাহিক ছুটি কোন কোন দিন?", "groq", RetrievalMethod: "qdrant-rerank"), CancellationToken.None);

        Assert.Contains("[1] weekly holidays Friday", seenMessages().Single(m => m.Role == "user").Content);
    }

    // ERP প্রশ্নে reranker score ≈০ → passage ঢোকে না, model ERP tool ডাকে।
    [Fact]
    public async Task PreRetrieval_WithRerank_LowRerankScore_DoesNotInjectPassages()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("unrelated", 0.0002)], passages: "[1] unrelated",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45, PreRetrieveMinRerankScore = 0.005 },
            bestVectorScore: 0.52, bestRerankScore: 0.0002); // vector cosine উচ্চ হলেও reranker বলছে অপ্রাসঙ্গিক

        await handler.Handle(new RunAgentCommand("জুলাইয়ের বিক্রি কত?", "groq", RetrievalMethod: "qdrant-rerank"), CancellationToken.None);

        Assert.Equal("জুলাইয়ের বিক্রি কত?", seenMessages().Single(m => m.Role == "user").Content);
    }

    // MinRerankScore = 0 (বন্ধ) হলে reranker score উপেক্ষা, আগের vector gate-ই (vector ০.৬০ ≥ ০.৪৫ → ঢোকে)।
    [Fact]
    public async Task PreRetrieval_RerankGateOff_FallsBackToVectorGate()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("x", 0.0002)], passages: "[1] p",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45, PreRetrieveMinRerankScore = 0 },
            bestVectorScore: 0.60, bestRerankScore: 0.0002);

        await handler.Handle(new RunAgentCommand("প্রশ্ন", "groq"), CancellationToken.None);

        Assert.Contains("[1] p", seenMessages().Single(m => m.Role == "user").Content);
    }

    // rerank চলেনি (BestRerankScore null) হলে gate-ও vector-এ: vector ০.৩০ < ০.৪৫ → ঢোকে না।
    [Fact]
    public async Task PreRetrieval_RerankNotUsed_UsesVectorGate()
    {
        var (handler, _, seenMessages) = KnowledgeBaseHandler(
            hits: [new ScoredHit("x", 0.30)], passages: "[1] p",
            new AgentSettings { Enabled = true, PreRetrieveMinScore = 0.45, PreRetrieveMinRerankScore = 0.005 },
            bestVectorScore: 0.30, bestRerankScore: null);

        await handler.Handle(new RunAgentCommand("প্রশ্ন", "groq"), CancellationToken.None);

        Assert.Equal("প্রশ্ন", seenMessages().Single(m => m.Role == "user").Content);
    }

    // user বেছে-নেওয়া পদ্ধতি trace-এ বসে (KB tool সেটা পড়ে) — না বাছলে null।
    [Fact]
    public async Task RetrievalMethod_IsPassedToTrace_AtRequestStart()
    {
        var trace = Substitute.For<IAgentRetrievalTrace>();
        trace.DocumentHits.Returns(Array.Empty<ScoredHit>());
        var provider = ToolProvider(out var llm);
        llm.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatToolResult("ok", [], "groq", "llama"));
        _resolver.Resolve("groq", null).Returns(provider);
        var settings = new AgentSettings { Enabled = true };
        var handler = new RunAgentCommandHandler(
            _resolver, _tools, _catalog, trace, _usage, settings,
            new MultiQuestionRunner(Substitute.For<IServiceScopeFactory>(), settings));

        await handler.Handle(new RunAgentCommand("প্রশ্ন", "groq", RetrievalMethod: "qdrant-rerank"), CancellationToken.None);

        trace.Received(1).Begin("qdrant-rerank");
    }

    [Fact]
    public async Task PreRetrieval_Disabled_DoesNotCallKnowledgeBaseTool()
    {
        var (handler, kb, _) = KnowledgeBaseHandler(
            hits: [new ScoredHit("x", 0.9)], passages: "p", new AgentSettings { Enabled = true, PreRetrieveDocuments = false });

        await handler.Handle(new RunAgentCommand("প্রশ্ন", "groq"), CancellationToken.None);

        await kb.DidNotReceive().ExecuteAsync(Arg.Any<System.Text.Json.JsonElement>(), Arg.Any<CancellationToken>());
    }

    private (RunAgentCommandHandler Handler, ITool Kb, Func<IReadOnlyList<ChatMessage>> SeenMessages) KnowledgeBaseHandler(
        IReadOnlyList<ScoredHit> hits, string passages, AgentSettings? settings = null, double? bestVectorScore = null,
        string? methodUsed = null, double? bestRerankScore = null)
    {
        var kb = Substitute.For<ITool>();
        kb.Name.Returns("search_knowledge_base");
        kb.Description.Returns("kb");
        kb.ParametersSchema.Returns(new { type = "object", properties = new { } });
        var trace = Substitute.For<IAgentRetrievalTrace>();
        trace.DocumentHits.Returns(hits);
        trace.BestVectorScore.Returns(bestVectorScore);
        trace.MethodUsed.Returns(methodUsed);
        trace.BestRerankScore.Returns(bestRerankScore);
        kb.ExecuteAsync(Arg.Any<System.Text.Json.JsonElement>(), Arg.Any<CancellationToken>()).Returns(passages);

        IReadOnlyList<ChatMessage>? seen = null;
        var provider = ToolProvider(out var llm);
        llm.CompleteWithToolsAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<ToolDefinition>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                seen = callInfo.ArgAt<IReadOnlyList<ChatMessage>>(0).ToList();
                return new ChatToolResult("ok", [], "groq", "llama");
            });
        _resolver.Resolve("groq", null).Returns(provider);

        var effective = settings ?? new AgentSettings { Enabled = true };
        var handler = new RunAgentCommandHandler(
            _resolver, [kb], _catalog, trace, _usage, effective,
            new MultiQuestionRunner(Substitute.For<IServiceScopeFactory>(), effective));
        return (handler, kb, () => seen!);
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
