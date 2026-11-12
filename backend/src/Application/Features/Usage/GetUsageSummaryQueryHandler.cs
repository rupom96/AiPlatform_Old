using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Usage;

public class GetUsageSummaryQueryHandler(IUsageReader usageReader)
    : IRequestHandler<GetUsageSummaryQuery, Result<UsageSummary>>
{
    public async Task<Result<UsageSummary>> Handle(GetUsageSummaryQuery request, CancellationToken cancellationToken)
    {
        var summary = await usageReader.GetSummaryAsync(cancellationToken);
        return summary;
    }
}
