using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Services;
using NSubstitute;

namespace AiPlatform.Application.Tests.Chat;

public class ConversationMemoryTests
{
    private readonly IConversationStore _store = Substitute.For<IConversationStore>();
    private readonly ILlmUsageRecorder _usage = Substitute.For<ILlmUsageRecorder>();

    private ConversationMemory Memory() => new(_store, _usage);

    [Fact]
    public void IsActive_RequiresEnabled_ConversationId_AndUser()
    {
        _store.IsEnabled.Returns(true);
        Assert.True(Memory().IsActive("c1", "u1"));
        Assert.False(Memory().IsActive(null, "u1"));
        Assert.False(Memory().IsActive("c1", null));

        _store.IsEnabled.Returns(false);
        Assert.False(Memory().IsActive("c1", "u1"));
    }

    [Fact]
    public async Task BuildMessages_PrependsSummary_ThenRecent_ThenNewMessage()
    {
        _store.GetAsync("u1", "c1", Arg.Any<CancellationToken>())
            .Returns(new ConversationState("আগের সারাংশ",
                [new ChatMessage("user", "hi"), new ChatMessage("assistant", "hello")]));

        var messages = await Memory().BuildMessagesAsync("u1", "c1", "next?", CancellationToken.None);

        Assert.Equal(4, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Contains("আগের সারাংশ", messages[0].Content);
        Assert.Equal("next?", messages[3].Content);
    }

    [Fact]
    public async Task PersistTurn_UnderMax_SavesWithoutSummarizing()
    {
        _store.MaxRecentMessages.Returns(10);
        _store.GetAsync("u1", "c1", Arg.Any<CancellationToken>()).Returns(ConversationState.Empty);
        var provider = Substitute.For<IChatProvider>();

        await Memory().PersistTurnAsync("u1", "c1", "q", "a", provider, CancellationToken.None);

        await _store.Received().SaveAsync("u1", "c1",
            Arg.Is<ConversationState>(s => s.Recent.Count == 2 && s.Summary == null), Arg.Any<CancellationToken>());
        await provider.DidNotReceive().CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistTurn_OverMax_Summarizes_AndTrims_AndRecordsUsage()
    {
        _store.MaxRecentMessages.Returns(2); // 2 আগের + 2 নতুন = 4 > 2 → summarize
        _store.GetAsync("u1", "c1", Arg.Any<CancellationToken>())
            .Returns(new ConversationState(null, [new ChatMessage("user", "q1"), new ChatMessage("assistant", "a1")]));

        var provider = Substitute.For<IChatProvider>();
        provider.CompleteAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<CancellationToken>())
            .Returns(new ChatResult("summary text", "gemini", "gemini-2.5-flash-lite"));

        await Memory().PersistTurnAsync("u1", "c1", "q2", "a2", provider, CancellationToken.None);

        _usage.Received().Record(Arg.Is<LlmUsageRecord>(r => r.Feature == "chat-summary"));
        await _store.Received().SaveAsync("u1", "c1",
            Arg.Is<ConversationState>(s => s.Summary == "summary text" && s.Recent.Count == 2), Arg.Any<CancellationToken>());
    }
}
