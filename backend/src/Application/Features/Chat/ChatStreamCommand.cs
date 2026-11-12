using MediatR;

namespace AiPlatform.Application.Features.Chat;

// প্রতিটা item = একটা token chunk (SSE)। Model দিলে ওই provider ওই model-এ (ollama model-picker)।
// ConversationId দিলে multi-turn memory (UserId scope)।
public record ChatStreamCommand(
    string Message, string? Provider, string? Model = null, string? ConversationId = null, string? UserId = null)
    : IStreamRequest<string>;
