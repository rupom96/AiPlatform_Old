using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Agent;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// এক বার্তার অনেক প্রশ্ন আলাদা run-এ ভেঙে উত্তর এক করা: ক্রম, সমান্তরাল সীমা, আংশিক ব্যর্থতা।
public class MultiQuestionRunnerTests
{
    private static MultiQuestionRunner Runner(FakeAgentHandler handler, int maxParallel = 4)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRequestHandler<RunAgentCommand, Result<AgentResponse>>>(_ => handler);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new MultiQuestionRunner(scopeFactory, new AgentSettings { MaxParallelQuestions = maxParallel });
    }

    private static QuestionSplit Split(params string[] questions) => new(questions, 0);

    [Fact]
    public async Task AnswersAreCombinedInQuestionOrder_EvenIfTheyFinishOutOfOrder()
    {
        // প্রথম প্রশ্ন ধীর, শেষটা দ্রুত — তবু উত্তর প্রশ্নের ক্রমে থাকতে হবে
        var handler = new FakeAgentHandler(message => message == "প্রথম?" ? 80 : 0);

        var result = await Runner(handler).RunAsync(
            Split("প্রথম?", "দ্বিতীয়?", "তৃতীয়?"), new RunAgentCommand("x", "ollama", "m"), CancellationToken.None);

        var lines = result.Value.Answer.Split("\n\n");
        Assert.Equal("1. প্রথম?\nউত্তর: প্রথম?", lines[0]);
        Assert.Equal("2. দ্বিতীয়?\nউত্তর: দ্বিতীয়?", lines[1]);
        Assert.Equal("3. তৃতীয়?\nউত্তর: তৃতীয়?", lines[2]);
    }

    [Fact]
    public async Task EachQuestionRunsWithSkipSplit_AndKeepsProviderAndModel()
    {
        var handler = new FakeAgentHandler();

        await Runner(handler).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama", "gemma4:e4b"), CancellationToken.None);

        Assert.All(handler.Commands, c =>
        {
            Assert.True(c.SkipSplit);               // নইলে অনন্ত ভাঙা
            Assert.Equal("ollama", c.Provider);
            Assert.Equal("gemma4:e4b", c.Model);
        });
        Assert.Equal(["এক?", "দুই?"], handler.Commands.Select(c => c.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ParallelismIsCapped()
    {
        var handler = new FakeAgentHandler(_ => 30);

        await Runner(handler, maxParallel: 3).RunAsync(
            Split(Enumerable.Range(1, 12).Select(i => $"প্রশ্ন {i}?").ToArray()),
            new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.InRange(handler.MaxInFlight, 2, 3);   // সমান্তরাল হয়েছে, কিন্তু সীমার বেশি নয়
    }

    // একটা প্রশ্ন ব্যর্থ হলে বাকিগুলোর উত্তর হারায় না
    [Fact]
    public async Task OneFailure_ShowsPlaceholder_OthersKept()
    {
        var handler = new FakeAgentHandler(failFor: "খারাপ?");

        var result = await Runner(handler).RunAsync(
            Split("ভালো?", "খারাপ?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("উত্তর: ভালো?", result.Value.Answer);
        Assert.Contains("উত্তর আনা যায়নি", result.Value.Answer);
    }

    [Fact]
    public async Task AllFailures_ReturnTheFirstError()
    {
        var handler = new FakeAgentHandler(failFor: "*");

        var result = await Runner(handler).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Agent.Fake", result.Error.Code);
    }

    [Fact]
    public async Task SkippedQuestions_AreMentionedInTheAnswer()
    {
        var result = await Runner(new FakeAgentHandler()).RunAsync(
            new QuestionSplit(["এক?", "দুই?"], Skipped: 3), new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.Contains("মোট 5টি প্রশ্ন ছিল", result.Value.Answer);
        Assert.Contains("বাকি 3টি", result.Value.Answer);
    }

    [Fact]
    public async Task ToolsAndHits_AreMergedWithoutDuplicates()
    {
        var handler = new FakeAgentHandler();

        var result = await Runner(handler).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.Equal(["get_sales"], result.Value.ToolsUsed);                       // দুই প্রশ্নেই এক tool → একবার
        var hit = Assert.Single(result.Value.ToolHits!);                            // একই label → সর্বোচ্চ score
        Assert.Equal("get_sales", hit.Label);
        Assert.Equal("ollama", result.Value.Provider);
    }

    // retrieval পদ্ধতি: সবাই একই হলে সেটা; কেউ ফল্লব্যাক করলে (মেলে না) null — "সব rerank চলেছে" মিথ্যা বলা যাবে না;
    // যে প্রশ্নে ডকুমেন্ট খোঁজাই হয়নি (null) সে হিসাবে ধরা হয় না।
    [Fact]
    public async Task RetrievalMethodUsed_IsCommonValue_OrNullWhenQuestionsDiffer()
    {
        var same = await Runner(new FakeAgentHandler(methodFor: _ => "qdrant-rerank")).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);
        var mixed = await Runner(new FakeAgentHandler(methodFor: q => q == "এক?" ? "qdrant-rerank" : "qdrant")).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);
        var oneErpOnly = await Runner(new FakeAgentHandler(methodFor: q => q == "এক?" ? "qdrant-rerank" : null)).RunAsync(
            Split("এক?", "দুই?"), new RunAgentCommand("x", "ollama"), CancellationToken.None);

        Assert.Equal("qdrant-rerank", same.Value.RetrievalMethodUsed);
        Assert.Null(mixed.Value.RetrievalMethodUsed);
        Assert.Equal("qdrant-rerank", oneErpOnly.Value.RetrievalMethodUsed);
    }

    // ---------------------------------------------------------------- fake

    private sealed class FakeAgentHandler(
        Func<string, int>? delayMsFor = null, string? failFor = null, Func<string, string?>? methodFor = null)
        : IRequestHandler<RunAgentCommand, Result<AgentResponse>>
    {
        private int _inFlight;
        private int _maxInFlight;
        private readonly List<RunAgentCommand> _commands = [];

        public int MaxInFlight => _maxInFlight;
        public IReadOnlyList<RunAgentCommand> Commands { get { lock (_commands) return [.. _commands]; } }

        public async Task<Result<AgentResponse>> Handle(RunAgentCommand request, CancellationToken cancellationToken)
        {
            lock (_commands) _commands.Add(request);

            var now = Interlocked.Increment(ref _inFlight);
            InterlockedMax(ref _maxInFlight, now);
            try
            {
                var delay = delayMsFor?.Invoke(request.Message) ?? 0;
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken);

                if (failFor == "*" || failFor == request.Message)
                    return Error.Failure("Agent.Fake", "boom");

                return new AgentResponse(
                    $"উত্তর: {request.Message}", ["get_sales"], 5,
                    [new ScoredHit("get_sales", request.Message.Length)], null, request.Provider, request.Model,
                    methodFor?.Invoke(request.Message));
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref target)))
                if (Interlocked.CompareExchange(ref target, value, current) == current)
                    return;
        }
    }
}
