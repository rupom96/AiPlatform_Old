using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// multi-turn memory orchestration — chat + stream দুই handler reuse করে (logic এক জায়গায়)।
public interface IConversationMemory
{
    // conversationId + userId আছে ও feature on → memory active।
    bool IsActive(string? conversationId, string? userId);

    // LLM-এ পাঠানোর message list: [summary] + সাম্প্রতিক turn + নতুন user message।
    Task<IReadOnlyList<ChatMessage>> BuildMessagesAsync(
        string userId, string conversationId, string userMessage, CancellationToken cancellationToken);

    // turn persist: user+assistant history-তে যোগ, MaxRecent ছাড়ালে overflow summarize (provider দিয়ে)।
    Task PersistTurnAsync(
        string userId, string conversationId, string userMessage, string assistantReply,
        IChatProvider provider, CancellationToken cancellationToken);
}
