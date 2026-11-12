using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Domain.Entities;
using MediatR;

namespace AiPlatform.Application.Features.Documents.Upload;

// document DB-তে সেভ (Pending) → queue-তে ঢুকিয়ে সাথে সাথে ফেরত। ingestion background-এ হয়।
public class UploadDocumentCommandHandler(
    IDocumentRepository repository,
    IIngestionQueue ingestionQueue,
    ICurrentUser currentUser)
    : IRequestHandler<UploadDocumentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(UploadDocumentCommand request, CancellationToken cancellationToken)
    {
        var document = new Document
        {
            TenantId = currentUser.TenantId,
            FileName = request.FileName,
            Content = request.Content,
            Status = DocumentStatus.Pending
        };

        await repository.AddAsync(document, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        await ingestionQueue.EnqueueAsync(document.Id, cancellationToken);

        return document.Id;
    }
}
