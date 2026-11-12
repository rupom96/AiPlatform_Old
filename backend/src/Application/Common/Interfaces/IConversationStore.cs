using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// conversation memory persistence (Redis)। summarize করা handler-এর কাজ; এটা শুধু get/save।
public interface IConversationStore
{
    bool IsEnabled { get; }

    // এর বেশি message জমলে handler পুরনোগুলো summarize করে trim করবে।
    int MaxRecentMessages { get; }

    Task<ConversationState> GetAsync(string userId, string conversationId, CancellationToken cancellationToken);
    Task SaveAsync(string userId, string conversationId, ConversationState state, CancellationToken cancellationToken);

    // user-এর সব conversation (sidebar), newest-first।
    Task<IReadOnlyList<ConversationSummary>> ListAsync(string userId, CancellationToken cancellationToken);
}
