using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Conversations;

public class ListConversationsQueryHandler(IConversationStore store, ICurrentUser currentUser)
    : IRequestHandler<ListConversationsQuery, Result<IReadOnlyList<ConversationSummary>>>
{
    public async Task<Result<IReadOnlyList<ConversationSummary>>> Handle(
        ListConversationsQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return Result.Success<IReadOnlyList<ConversationSummary>>([]);

        var conversations = await store.ListAsync(currentUser.UserId, cancellationToken);
        return Result.Success(conversations);
    }
}
