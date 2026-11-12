using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Audit;

public class ListAuditQueryHandler(IAuditLog auditLog)
    : IRequestHandler<ListAuditQuery, Result<PagedResult<AuditEntryResponse>>>
{
    public async Task<Result<PagedResult<AuditEntryResponse>>> Handle(
        ListAuditQuery request, CancellationToken cancellationToken)
    {
        var (items, total) = await auditLog.ListAsync(request.Page, request.PageSize, cancellationToken);

        var responses = items
            .Select(a => new AuditEntryResponse(a.Id, a.UserId, a.Action, a.Succeeded, a.CreatedAtUtc))
            .ToList();

        return new PagedResult<AuditEntryResponse>(responses, request.Page, request.PageSize, total);
    }
}
