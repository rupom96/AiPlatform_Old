using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.Upload;

public record UploadDocumentCommand(string FileName, string Content)
    : IRequest<Result<Guid>>, IAuditableRequest
{
    public string AuditAction => $"Document.Upload:{FileName}";
}
