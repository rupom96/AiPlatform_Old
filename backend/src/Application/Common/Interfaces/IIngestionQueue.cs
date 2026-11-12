using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// Upload → background ingestion decouple করে (Redis Streams; durable, at-least-once)।
// process সফল হলে AckAsync — নইলে message pending থেকে যায় (crash-এ redeliver)।
public interface IIngestionQueue
{
    Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken);

    // এক কাজ পড়ে (নতুন বা reclaim-করা pending)। খালি হলে null → caller একটু অপেক্ষা করে আবার।
    Task<IngestionMessage?> ReadAsync(CancellationToken cancellationToken);

    Task AckAsync(string messageId, CancellationToken cancellationToken);
}
