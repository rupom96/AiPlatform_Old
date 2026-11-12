using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Documents.GetStatus;

public record GetDocumentStatusQuery(Guid Id) : IRequest<Result<DocumentStatusResponse>>;
