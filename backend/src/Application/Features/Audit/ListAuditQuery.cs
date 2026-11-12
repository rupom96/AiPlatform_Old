using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Audit;

// newest-first paged audit trail (current tenant-এর, query filter)।
public record ListAuditQuery(int Page, int PageSize)
    : IRequest<Result<PagedResult<AuditEntryResponse>>>;
