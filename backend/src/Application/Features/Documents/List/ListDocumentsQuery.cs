using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.List;

// newest-first paged document list। soft-deleted বাদ।
public record ListDocumentsQuery(int Page, int PageSize)
    : IRequest<Result<PagedResult<DocumentStatusResponse>>>;
