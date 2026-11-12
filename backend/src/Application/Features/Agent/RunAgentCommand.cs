using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Agent;

// tool-agent: LLM tool call করে, execute হয়, feed back — final answer পর্যন্ত। Provider tool-capable হতে হবে।
// Model = ollama model-picker override (chat path-এর মতো)।
public record RunAgentCommand(string Message, string? Provider, string? Model = null)
    : IRequest<Result<AgentResponse>>;

// ToolsUsed = কোন tool কল হয়েছে। LatencyMs = সব LLM turn-এর যোগফল।
// ToolHits = aiplatform_tools scores; DocumentHits = docs search scores (KB tool চললে)।
public record AgentResponse(
    string Answer,
    IReadOnlyList<string> ToolsUsed,
    long LatencyMs = 0,
    IReadOnlyList<ScoredHit>? ToolHits = null,
    IReadOnlyList<ScoredHit>? DocumentHits = null);
