using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Conversations;
using NSubstitute;

namespace AiPlatform.Application.Tests.Conversations;

public class ConversationsTests
{
    private readonly IConversationStore _store = Substitute.For<IConversationStore>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    [Fact]
    public async Task List_ReturnsUserConversations()
    {
        _currentUser.UserId.Returns("u1");
        IReadOnlyList<ConversationSummary> convos = [new("c1", "প্রথম প্রশ্ন", DateTime.UtcNow)];
        _store.ListAsync("u1", Arg.Any<CancellationToken>()).Returns(convos);

        var result = await new ListConversationsQueryHandler(_store, _currentUser)
            .Handle(new ListConversationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("c1", result.Value[0].ConversationId);
    }

    [Fact]
    public async Task List_NoUser_ReturnsEmpty()
    {
        _currentUser.UserId.Returns((string?)null);

        var result = await new ListConversationsQueryHandler(_store, _currentUser)
            .Handle(new ListConversationsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Get_ExistingConversation_ReturnsMessages()
    {
        _currentUser.UserId.Returns("u1");
        _store.GetAsync("u1", "c1", Arg.Any<CancellationToken>())
            .Returns(new ConversationState("summary", [new ChatMessage("user", "hi")]));

        var result = await new GetConversationQueryHandler(_store, _currentUser)
            .Handle(new GetConversationQuery("c1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("summary", result.Value.Summary);
        Assert.Single(result.Value.Messages);
    }

    [Fact]
    public async Task Get_MissingConversation_ReturnsNotFound()
    {
        _currentUser.UserId.Returns("u1");
        _store.GetAsync("u1", "missing", Arg.Any<CancellationToken>()).Returns(ConversationState.Empty);

        var result = await new GetConversationQueryHandler(_store, _currentUser)
            .Handle(new GetConversationQuery("missing"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
