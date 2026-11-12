using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AiPlatform.Infrastructure.Ingestion;

// Redis Streams consumer-group queue। XADD (enqueue) → XREADGROUP (read) → XACK (done)।
// at-least-once: unacked message pending থাকে; crash/dead-worker হলে XAUTOCLAIM reclaim করে।
public class RedisStreamIngestionQueue : IIngestionQueue
{
    private const string DocIdField = "documentId";

    private readonly IDatabase _db;
    private readonly IngestionQueueSettings _settings;
    private readonly string _consumer;       // per-process unique → multi-replica-এ আলাদা consumer
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _groupReady;

    public RedisStreamIngestionQueue(IConnectionMultiplexer redis, IOptions<IngestionQueueSettings> options)
    {
        _db = redis.GetDatabase();
        _settings = options.Value;
        _consumer = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    public async Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureGroupAsync();
        await _db.StreamAddAsync(_settings.StreamKey, DocIdField, documentId.ToString());
    }

    public async Task<IngestionMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        await EnsureGroupAsync();

        // ১) আগে stale pending reclaim (crashed/dead worker-এর unacked) → restart-safe।
        var claim = await _db.StreamAutoClaimAsync(
            _settings.StreamKey, _settings.GroupName, _consumer, _settings.MinIdleMs, "0-0", count: 1);
        if (TryMap(claim.ClaimedEntries, out var reclaimed))
            return reclaimed;

        // ২) নতুন message।
        var entries = await _db.StreamReadGroupAsync(
            _settings.StreamKey, _settings.GroupName, _consumer, ">", count: 1);
        return TryMap(entries, out var fresh) ? fresh : null;
    }

    public Task AckAsync(string messageId, CancellationToken cancellationToken) =>
        _db.StreamAcknowledgeAsync(_settings.StreamKey, _settings.GroupName, messageId);

    private bool TryMap(StreamEntry[]? entries, out IngestionMessage? message)
    {
        message = null;
        if (entries is null || entries.Length == 0)
            return false;

        var entry = entries[0];
        var raw = entry.Values.FirstOrDefault(v => v.Name == DocIdField).Value;
        if (!raw.IsNullOrEmpty && Guid.TryParse(raw.ToString(), out var documentId))
        {
            message = new IngestionMessage(entry.Id!, documentId);
            return true;
        }

        // malformed (আমাদের producer-এ হওয়ার কথা না) — ack করে drop, নইলে চিরকাল reclaim loop।
        _db.StreamAcknowledge(_settings.StreamKey, _settings.GroupName, entry.Id!);
        return false;
    }

    private async Task EnsureGroupAsync()
    {
        if (_groupReady) return;
        await _initLock.WaitAsync();
        try
        {
            if (_groupReady) return;
            try
            {
                await _db.StreamCreateConsumerGroupAsync(
                    _settings.StreamKey, _settings.GroupName, StreamPosition.NewMessages, createStream: true);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                // group আগেই আছে — স্বাভাবিক।
            }
            _groupReady = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
