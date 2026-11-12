using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiPlatform.Infrastructure.Idempotency;

// Redis SET NX দিয়ে slot দখল (atomic) → concurrent duplicate নিরাপদে আলাদা করা যায়।
//   key = processing sentinel  → চলছে
//   key = StoredResponse JSON  → শেষ, এটাই ফেরত
public class RedisIdempotencyStore(IConnectionMultiplexer redis, IOptions<IdempotencySettings> options)
    : IIdempotencyStore
{
    private const string Processing = "processing";
    private readonly IdempotencySettings _settings = options.Value;
    private TimeSpan Ttl => TimeSpan.FromHours(_settings.TtlHours);

    public bool IsEnabled => _settings.Enabled;

    public async Task<IdempotencyBegin> BeginAsync(string key, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();

        // atomic acquire — সফল হলে আমরাই প্রথম।
        if (await db.StringSetAsync(key, Processing, Ttl, When.NotExists))
            return new IdempotencyBegin(IdempotencyState.Started, null);

        var existing = await db.StringGetAsync(key);
        // ponytail: acquire আর GET-এর মাঝে key expire হলে (বিরল) InProgress ধরি — client retry করবে।
        if (existing.IsNullOrEmpty || existing == Processing)
            return new IdempotencyBegin(IdempotencyState.InProgress, null);

        var stored = JsonSerializer.Deserialize<StoredResponse>(existing.ToString());
        return new IdempotencyBegin(IdempotencyState.Completed, stored);
    }

    public Task CompleteAsync(string key, StoredResponse response, CancellationToken cancellationToken) =>
        // processing → stored response দিয়ে overwrite, TTL reset।
        redis.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(response), Ttl);

    public Task AbandonAsync(string key, CancellationToken cancellationToken) =>
        redis.GetDatabase().KeyDeleteAsync(key);
}
