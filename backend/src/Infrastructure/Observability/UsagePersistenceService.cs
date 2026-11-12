using AiPlatform.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Infrastructure.Observability;

// queue থেকে usage record নিয়ে DB-তে persist করে (request path-এর বাইরে)।
public class UsagePersistenceService(
    IServiceProvider serviceProvider,
    UsageQueue queue,
    ILogger<UsagePersistenceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var usage = await queue.DequeueAsync(stoppingToken);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.LlmUsages.Add(usage);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to persist LLM usage");
            }
        }
    }
}
