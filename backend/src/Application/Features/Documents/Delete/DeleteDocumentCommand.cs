using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.Delete;

// soft-delete — সারি থাকে, শুধু IsDeleted flag হয়।
public record DeleteDocumentCommand(Guid Id) : IRequest<Result>, IAuditableRequest
{
    public string AuditAction => $"Document.Delete:{Id}";
}
