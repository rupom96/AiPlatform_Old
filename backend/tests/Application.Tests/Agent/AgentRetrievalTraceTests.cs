using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Agent;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

// tool একটা async মেথড; তার ভেতরে সেট করা hits যেন caller (handler) পড়তে পারে — আগে AsyncLocal-এর কারণে পারত না।
public class AgentRetrievalTraceTests
{
    [Fact]
    public async Task HitsSetInsideAwaitedAsyncMethod_AreVisibleToCaller()
    {
        var trace = new AgentRetrievalTrace();
        trace.Clear();

        await SetLaterAsync(trace);

        Assert.Single(trace.DocumentHits);
        Assert.Equal("chunk", trace.DocumentHits[0].Label);
    }

    [Fact]
    public async Task Clear_ResetsHits_BetweenRequests()
    {
        var trace = new AgentRetrievalTrace();
        trace.Clear();
        await SetLaterAsync(trace);

        trace.Clear();

        Assert.Empty(trace.DocumentHits);
    }

    private static async Task SetLaterAsync(AgentRetrievalTrace trace)
    {
        await Task.Yield(); // বাস্তবের মতো: await-এর পরে সেট
        trace.SetDocumentHits([new ScoredHit("chunk", 0.9)]);
    }
}
