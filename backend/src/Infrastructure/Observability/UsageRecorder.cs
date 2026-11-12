using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Domain.Entities;
using AiPlatform.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Observability;

// cost হিসাব করে + structured log + queue-তে enqueue (persist background-এ)।
public class UsageRecorder(
    IOptions<LlmSettings> options,
    UsageQueue queue,
    ILogger<UsageRecorder> logger) : ILlmUsageRecorder
{
    private readonly LlmSettings _settings = options.Value;

    public void Record(LlmUsageRecord record)
    {
        var cost = CalculateCost(record.Model, record.InputTokens, record.OutputTokens);

        queue.Enqueue(new LlmUsage
        {
            Feature = record.Feature,
            Provider = record.Provider,
            Model = record.Model,
            InputTokens = record.InputTokens,
            OutputTokens = record.OutputTokens,
            EstimatedCostUsd = cost,
            LatencyMs = record.LatencyMs,
        });

        logger.LogInformation(
            "LLM usage {Feature} {Provider}/{Model} in={In} out={Out} cost=${Cost} {Latency}ms",
            record.Feature, record.Provider, record.Model, record.InputTokens, record.OutputTokens, cost, record.LatencyMs);
    }

    private decimal CalculateCost(string model, int inputTokens, int outputTokens)
    {
        if (!_settings.Pricing.TryGetValue(model, out var pricing))
            return 0m;
        return inputTokens / 1_000_000m * pricing.InputPerMillion
             + outputTokens / 1_000_000m * pricing.OutputPerMillion;
    }
}
