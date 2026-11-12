using AiPlatform.Application.Common.Ingestion;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Application.Features.Documents.Delete;

// SQL-এ soft-delete + Qdrant থেকে ঐ document-এর chunk মুছে ফেলা।
// আগে শুধু soft-delete হতো, তাই মোছা ডকুমেন্টের chunk RAG-এ থেকে যেত আর সূত্র হিসেবে দেখাত।
public class DeleteDocumentCommandHandler(
    IDocumentRepository repository,
    IVectorStore vectorStore,
    ILogger<DeleteDocumentCommandHandler> logger)
    : IRequestHandler<DeleteDocumentCommand, Result>
{
    public async Task<Result> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(request.Id, cancellationToken);
        if (document is null)
            return Error.NotFound("Document.NotFound", "Document not found.");

        // vector আগে, SQL পরে। উল্টো করলে vector delete ব্যর্থ হলে SQL-এ মোছা দেখাত অথচ
        // chunk রয়ে যেত — আর কেউ ওগুলোর খোঁজ রাখত না। এভাবে ব্যর্থ হলে সারি থাকে, আবার চেষ্টা করা যায়।
        // id হুবহু ingestion-এর নিয়মেই বানানো, তাই filter নয় — ঠিক ঐ document-এর chunk-ই মোছে।
        // একই নামে দুটো ডকুমেন্ট থাকলেও একটার delete অন্যটাকে ছোঁয় না।
        await vectorStore.DeleteAsync(
            IngestionPointId.ForDocument(document.Id, document.TotalChunks), cancellationToken);

        document.IsDeleted = true;
        document.DeletedAtUtc = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Deleted document {DocumentId} ({FileName}) and its {Chunks} chunks",
            document.Id, document.FileName, document.TotalChunks);

        return Result.Success();
    }
}
