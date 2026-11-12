using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Conversations;

// current user-এর সব conversation (sidebar), newest-first।
public record ListConversationsQuery : IRequest<Result<IReadOnlyList<ConversationSummary>>>;
