using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Usage;

public record GetUsageSummaryQuery : IRequest<Result<UsageSummary>>;
