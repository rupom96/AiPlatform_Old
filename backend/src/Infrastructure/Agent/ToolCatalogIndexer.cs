using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Infrastructure.Agent;

// App start-এ সব ITool → Qdrant tools collection।
public class ToolCatalogIndexer(
    IServiceScopeFactory scopeFactory,
    ILogger<ToolCatalogIndexer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tools = scope.ServiceProvider.GetServices<ITool>().ToList();
            var catalog = scope.ServiceProvider.GetRequiredService<IToolCatalog>();
            await catalog.SyncAsync(tools, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Startup tool-catalog index skipped — agent will retry on first request");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
