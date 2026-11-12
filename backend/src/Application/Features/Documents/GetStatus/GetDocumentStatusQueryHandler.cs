using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.GetStatus;

public class GetDocumentStatusQueryHandler(IDocumentRepository repository)
    : IRequestHandler<GetDocumentStatusQuery, Result<DocumentStatusResponse>>
{
    public async Task<Result<DocumentStatusResponse>> Handle(GetDocumentStatusQuery request, CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(request.Id, cancellationToken);
        if (document is null)
            return Error.NotFound("Document.NotFound", "Document not found.");

        return new DocumentStatusResponse(
            document.Id,
            document.FileName,
            document.Status.ToString(),
            document.TotalChunks,
            document.ProcessedChunks,
            document.ErrorMessage);
    }
}
