using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Domain.Entities;
using MediatR;

namespace AiPlatform.Application.Common.Behaviors;

// IAuditableRequest command handler-এর পর audit entry লেখে (user/tenant/action/outcome)।
// ValidationBehavior-এর পরে বসে → শুধু valid request audit হয়; business-fail হলে Succeeded=false।
public class AuditBehavior<TRequest, TResponse>(IAuditLog auditLog, ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (request is IAuditableRequest auditable)
        {
            // Result (বা Result<T>) হলে outcome পড়ি; নইলে সফল ধরি।
            var succeeded = response is not Result result || result.IsSuccess;

            await auditLog.WriteAsync(new AuditEntry
            {
                TenantId = currentUser.TenantId,
                UserId = currentUser.UserId,
                Action = auditable.AuditAction,
                Succeeded = succeeded,
            }, cancellationToken);
        }

        return response;
    }
}
