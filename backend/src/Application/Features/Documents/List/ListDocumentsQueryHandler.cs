using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.List;

public class ListDocumentsQueryHandler(IDocumentRepository repository)
    : IRequestHandler<ListDocumentsQuery, Result<PagedResult<DocumentStatusResponse>>>
{
    public async Task<Result<PagedResult<DocumentStatusResponse>>> Handle(
        ListDocumentsQuery request, CancellationToken cancellationToken)
    {
        var (items, total) = await repository.ListAsync(request.Page, request.PageSize, cancellationToken);

        var responses = items
            .Select(d => new DocumentStatusResponse(
                d.Id, d.FileName, d.Status.ToString(), d.TotalChunks, d.ProcessedChunks, d.ErrorMessage))
            .ToList();

        return new PagedResult<DocumentStatusResponse>(responses, request.Page, request.PageSize, total);
    }
}
