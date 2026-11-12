using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiPlatform.Infrastructure.Conversation;

// conversation state Redis-এ JSON হিসেবে; key user-scoped তাই cross-user access হয় না।
//   conv:{userId}:{conversationId} → ConversationState         (TTL শেষ activity থেকে slide)
//   conv:index:{userId}           → hash{convId → {title,updatedAt}}  (sidebar list-এর জন্য)
public class RedisConversationStore(IConnectionMultiplexer redis, IOptions<ConversationSettings> options)
    : IConversationStore
{
    private readonly ConversationSettings _settings = options.Value;

    public bool IsEnabled => _settings.Enabled;
    public int MaxRecentMessages => _settings.MaxRecentMessages;

    private static string Key(string userId, string conversationId) => $"conv:{userId}:{conversationId}";
    private static string IndexKey(string userId) => $"conv:index:{userId}";

    // index hash-এ প্রতি conversation-এর meta।
    private record IndexEntry(string Title, DateTime UpdatedAtUtc);

    public async Task<ConversationState> GetAsync(string userId, string conversationId, CancellationToken cancellationToken)
    {
        var json = await redis.GetDatabase().StringGetAsync(Key(userId, conversationId));
        if (json.IsNullOrEmpty)
            return ConversationState.Empty;

        return JsonSerializer.Deserialize<ConversationState>(json.ToString()) ?? ConversationState.Empty;
    }

    public async Task SaveAsync(string userId, string conversationId, ConversationState state, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        var ttl = TimeSpan.FromHours(_settings.TtlHours);

        // state — TTL slide (শেষ turn থেকে বাঁচে)।
        await db.StringSetAsync(Key(userId, conversationId), JsonSerializer.Serialize(state), ttl);

        // index — title প্রথমবার set (first user message); পরে শুধু updatedAt।
        var indexKey = IndexKey(userId);
        var existing = await db.HashGetAsync(indexKey, conversationId);
        var title = existing.IsNullOrEmpty
            ? MakeTitle(state)
            : JsonSerializer.Deserialize<IndexEntry>(existing.ToString())!.Title;

        await db.HashSetAsync(indexKey, conversationId,
            JsonSerializer.Serialize(new IndexEntry(title, DateTime.UtcNow)));
        await db.KeyExpireAsync(indexKey, ttl);
    }

    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var entries = await redis.GetDatabase().HashGetAllAsync(IndexKey(userId));

        return entries
            .Select(e => (Id: e.Name.ToString(), Meta: JsonSerializer.Deserialize<IndexEntry>(e.Value.ToString())))
            .Where(x => x.Meta is not null)
            .Select(x => new ConversationSummary(x.Id, x.Meta!.Title, x.Meta.UpdatedAtUtc))
            .OrderByDescending(s => s.UpdatedAtUtc)
            .ToList();
    }

    // প্রথম user message = শিরোনাম (৬০ char), না থাকলে placeholder।
    private static string MakeTitle(ConversationState state)
    {
        var first = state.Recent.FirstOrDefault(m => m.Role == "user")?.Content ?? "New conversation";
        return first.Length > 60 ? first[..60] : first;
    }
}
