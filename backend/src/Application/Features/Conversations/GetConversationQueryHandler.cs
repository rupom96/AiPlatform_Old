using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Conversations;

public class GetConversationQueryHandler(IConversationStore store, ICurrentUser currentUser)
    : IRequestHandler<GetConversationQuery, Result<ConversationDetailResponse>>
{
    public async Task<Result<ConversationDetailResponse>> Handle(
        GetConversationQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.UserId))
            return Error.Unauthorized("Conversation.NoUser", "No authenticated user.");

        var state = await store.GetAsync(currentUser.UserId, request.ConversationId, cancellationToken);
        if (state.Recent.Count == 0 && state.Summary is null)
            return Error.NotFound("Conversation.NotFound", "Conversation not found or expired.");

        return new ConversationDetailResponse(request.ConversationId, state.Summary, state.Recent);
    }
}
