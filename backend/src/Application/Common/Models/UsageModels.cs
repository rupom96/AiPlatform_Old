namespace AiPlatform.Application.Common.Models;

public record UsageByModel(
    string Provider,
    string Model,
    int Requests,
    long InputTokens,
    long OutputTokens,
    decimal CostUsd);

public record UsageSummary(
    decimal TotalCostUsd,
    int TotalRequests,
    long TotalInputTokens,
    long TotalOutputTokens,
    IReadOnlyList<UsageByModel> ByModel);
