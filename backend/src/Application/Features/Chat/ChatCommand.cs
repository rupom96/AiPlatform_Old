using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Chat;

// Provider null হলে config-এর default। Model দিলে ওই provider ওই model-এ (ollama model-picker)।
// ConversationId দিলে multi-turn memory চালু (UserId scope)।
public record ChatCommand(
    string Message, string? Provider, string? Model = null, string? ConversationId = null, string? UserId = null)
    : IRequest<Result<ChatResponse>>;
