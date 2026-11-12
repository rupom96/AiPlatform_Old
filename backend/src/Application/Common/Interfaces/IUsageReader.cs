using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

public interface IUsageReader
{
    Task<UsageSummary> GetSummaryAsync(CancellationToken cancellationToken);
}
