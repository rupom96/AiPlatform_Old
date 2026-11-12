namespace AiPlatform.Domain.Entities;

// প্রতি LLM call-এর observability record — cost/token/latency dashboard-এর ভিত্তি।
public class LlmUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Feature { get; set; } = string.Empty;   // chat | analyze | rag
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public long LatencyMs { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
