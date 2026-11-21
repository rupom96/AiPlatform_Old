using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Agent;

// tool-agent: LLM tool call করে, execute হয়, feed back — final answer পর্যন্ত। Provider tool-capable হতে হবে।
// Model = ollama model-picker override (chat path-এর মতো)।
// SkipSplit = true হলে বার্তা আর ভাঙা হয় না (বহু-প্রশ্ন ভেঙে চালানো এক-একটা প্রশ্ন, নইলে অনন্ত পুনরাবৃত্তি)।
// RetrievalMethod = ডকুমেন্ট খোঁজার ঐচ্ছিক পদ্ধতি (RetrievalMethods): null/"default" = আগের আচরণ; hybrid/rerank তুলনার জন্য।
public record RunAgentCommand(
    string Message, string? Provider, string? Model = null, bool SkipSplit = false, string? RetrievalMethod = null)
    : IRequest<Result<AgentResponse>>;

// ToolsUsed = কোন tool কল হয়েছে। LatencyMs = সব LLM turn-এর যোগফল।
// ToolHits = aiplatform_tools scores; DocumentHits = docs search scores (KB tool চললে)।
// Provider/Model = শেষ LLM call আসলে যে provider ও model দিল (fallback chain-এ কে উত্তর দিল তা বোঝার জন্য)।
// RetrievalMethodUsed = ডকুমেন্ট খোঁজা আসলে যে পদ্ধতিতে হলো (null = খোঁজা হয়নি)। বাছা পদ্ধতি ব্যর্থ হলে (যেমন reranker
// বন্ধ) এটা বাছা পদ্ধতি থেকে আলাদা — UI তখন সতর্ক করে, নইলে user ভাবে rerank-এই উত্তর এসেছে।
public record AgentResponse(
    string Answer,
    IReadOnlyList<string> ToolsUsed,
    long LatencyMs = 0,
    IReadOnlyList<ScoredHit>? ToolHits = null,
    IReadOnlyList<ScoredHit>? DocumentHits = null,
    string? Provider = null,
    string? Model = null,
    string? RetrievalMethodUsed = null);
