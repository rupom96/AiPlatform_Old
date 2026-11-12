namespace AiPlatform.Application.Common.Interfaces;

// এক LLM call-এর observability তথ্য (handler enqueue করে; cost recorder হিসাব করে)।
public record LlmUsageRecord(
    string Feature,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    long LatencyMs);

// non-blocking record — request path-এ DB latency যোগ করে না (background-এ persist হয়)।
public interface ILlmUsageRecorder
{
    void Record(LlmUsageRecord usage);
}
