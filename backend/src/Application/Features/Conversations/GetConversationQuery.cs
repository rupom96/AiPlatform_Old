using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Conversations;

public record GetConversationQuery(string ConversationId) : IRequest<Result<ConversationDetailResponse>>;

// detail view — summary + সাম্প্রতিক message (memory যা রেখেছে; পুরনো turn summary-তে গেছে)।
public record ConversationDetailResponse(string ConversationId, string? Summary, IReadOnlyList<ChatMessage> Messages);
