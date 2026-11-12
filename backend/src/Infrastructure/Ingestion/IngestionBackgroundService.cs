using AiPlatform.Application.Common.Ingestion;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Text;
using AiPlatform.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Ingestion;

// queue থেকে document নিয়ে chunk→embed→Qdrant upsert; status/progress DB-তে update করে।
// at-least-once: সফল/business-fail হলে ack; crash হলে ack হয় না → redeliver → idempotent reprocess।
public class IngestionBackgroundService(
    IServiceProvider serviceProvider,
    IIngestionQueue queue,
    IEmbeddingProvider embeddingProvider,
    IVectorStore vectorStore,
    IOptions<IngestionQueueSettings> options,
    ILogger<IngestionBackgroundService> logger)
    : BackgroundService
{
    private readonly int _pollDelayMs = options.Value.PollDelayMs;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IngestionMessage? message;
            try
            {
                message = await queue.ReadAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingestion queue read failed");
                await Task.Delay(_pollDelayMs, stoppingToken);
                continue;
            }

            if (message is null)
            {
                await Task.Delay(_pollDelayMs, stoppingToken);
                continue;
            }

            try
            {
                await ProcessAsync(message.DocumentId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ingestion failed for document {DocumentId}", message.DocumentId);
                await MarkFailedAsync(message.DocumentId, ex.Message, stoppingToken);
                // business/transient fail → ack (infinite loop এড়াতে); শুধু crash-এ redeliver হয়।
            }

            await queue.AckAsync(message.MessageId, stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid documentId, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var document = await repository.GetAsync(documentId, cancellationToken);
        if (document is null) return;

        // idempotent: redelivery-তে আগেই শেষ হওয়া doc আবার process করি না।
        if (document.Status == DocumentStatus.Completed) return;

        document.Status = DocumentStatus.Processing;
        // প্রতিটা chunk-এর মাথায় "[ফাইলের নাম > heading]" — নইলে heading-এর নিচের লাইনগুলো
        // প্রসঙ্গহীন হয়ে যায় আর খোঁজায় হেরে যায় (TextChunker-এ কারণটা লেখা আছে)।
        var chunks = TextChunker.ChunkWithContext(
            document.Content, Path.GetFileNameWithoutExtension(document.FileName));
        document.TotalChunks = chunks.Count;
        document.ProcessedChunks = 0;
        await repository.SaveChangesAsync(cancellationToken);

        var ensured = false;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var vector = await embeddingProvider.EmbedAsync(chunk, EmbeddingTaskType.RetrievalDocument, cancellationToken);
            if (!ensured)
            {
                await vectorStore.EnsureCollectionAsync(vector.Length, cancellationToken);
                ensured = true;
            }
            // deterministic id → reprocess-এ overwrite, duplicate vector নয়।
            await vectorStore.UpsertAsync(
                [new VectorPoint(IngestionPointId.For(documentId, i), vector, chunk, document.FileName)], cancellationToken);

            document.ProcessedChunks++;
            await repository.SaveChangesAsync(cancellationToken);
        }

        document.Status = DocumentStatus.Completed;
        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Ingested document {DocumentId}: {Chunks} chunks", documentId, chunks.Count);
    }

    private async Task MarkFailedAsync(Guid documentId, string error, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var document = await repository.GetAsync(documentId, cancellationToken);
            if (document is null) return;

            document.Status = DocumentStatus.Failed;
            document.ErrorMessage = error.Length > 2000 ? error[..2000] : error;
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not mark document {DocumentId} as failed", documentId);
        }
    }
}
