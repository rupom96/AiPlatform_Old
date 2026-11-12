using System.Threading.Channels;
using AiPlatform.Domain.Entities;

namespace AiPlatform.Infrastructure.Observability;

// LLM usage record background-এ persist করার জন্য in-memory queue (request path block করে না)।
public class UsageQueue
{
    private readonly Channel<LlmUsage> _channel = Channel.CreateUnbounded<LlmUsage>();

    public void Enqueue(LlmUsage usage) => _channel.Writer.TryWrite(usage);

    public ValueTask<LlmUsage> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
