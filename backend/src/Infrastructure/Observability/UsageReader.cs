using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AiPlatform.Infrastructure.Observability;

public class UsageReader(AppDbContext dbContext) : IUsageReader
{
    public async Task<UsageSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        // anonymous type-এ aggregate (EF translate করতে পারে), তারপর memory-তে record-এ map
        var grouped = await dbContext.LlmUsages
            .GroupBy(u => new { u.Provider, u.Model })
            .Select(g => new
            {
                g.Key.Provider,
                g.Key.Model,
                Requests = g.Count(),
                InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens),
                CostUsd = g.Sum(x => x.EstimatedCostUsd),
            })
            .ToListAsync(cancellationToken);

        var byModel = grouped
            .OrderByDescending(x => x.CostUsd)
            .Select(x => new UsageByModel(x.Provider, x.Model, x.Requests, x.InputTokens, x.OutputTokens, x.CostUsd))
            .ToList();

        return new UsageSummary(
            byModel.Sum(x => x.CostUsd),
            byModel.Sum(x => x.Requests),
            byModel.Sum(x => x.InputTokens),
            byModel.Sum(x => x.OutputTokens),
            byModel);
    }
}
