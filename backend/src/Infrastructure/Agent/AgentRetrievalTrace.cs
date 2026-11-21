using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Infrastructure.Agent;

// Singleton-safe: AsyncLocal per request — handler শুরুতে Begin() করে, KnowledgeBaseSearchTool লেখে, handler পড়ে।
//
// ⚠️ AsyncLocal-এ সরাসরি মান রাখলে চলে না: tool একটা async মেথড, তার ভেতরে `Current.Value = ...` করলে সেটা
// ফেরার সময় হারিয়ে যায় (ExecutionContext-এর বদল caller-এ ফেরে না) — handler তাই সবসময় খালি দেখত।
// তাই AsyncLocal-এ রাখি একটা বদলানো-যায় এমন Holder; Begin()/Clear() (handler-এর context-এ) তাকে বসায়, আর ভেতরের
// মেথড সেই *একই object*-এ লেখে, ফলে handler পড়তে পারে। একই কারণে RequestedMethod-ও Holder-এ — Begin-এর মান
// নিচের (child) async মেথডে পৌঁছায়, আর প্রতি request-এর নিজস্ব Holder বলে সমান্তরাল প্রশ্নে মিশে যায় না।
public class AgentRetrievalTrace : IAgentRetrievalTrace
{
    private sealed class Holder
    {
        public string? RequestedMethod { get; init; }
        public IReadOnlyList<ScoredHit> Hits { get; set; } = [];
        public double? BestVectorScore { get; set; }
        public string? MethodUsed { get; set; }
        public double? BestRerankScore { get; set; }
    }

    private static readonly AsyncLocal<Holder?> Current = new();

    public string? RequestedMethod => Current.Value?.RequestedMethod;

    public IReadOnlyList<ScoredHit> DocumentHits => Current.Value?.Hits ?? [];

    public double? BestVectorScore => Current.Value?.BestVectorScore;

    public void SetDocumentHits(IReadOnlyList<ScoredHit> hits)
    {
        // Begin()/Clear() না ডেকে এলে (যেমন tool সরাসরি চালালে) নিজের holder বসাই; সেটা শুধু এই context-এ দেখা যায়।
        var holder = Current.Value ??= new Holder();
        holder.Hits = hits;
    }

    public double? BestRerankScore => Current.Value?.BestRerankScore;

    public void SetBestRerankScore(double? score)
    {
        var holder = Current.Value ??= new Holder();
        holder.BestRerankScore = score;
    }

    public string? MethodUsed => Current.Value?.MethodUsed;

    public void SetMethodUsed(string? method)
    {
        var holder = Current.Value ??= new Holder();
        holder.MethodUsed = method;
    }

    public void SetBestVectorScore(double? score)
    {
        var holder = Current.Value ??= new Holder();
        holder.BestVectorScore = score;
    }

    public void Begin(string? requestedMethod) => Current.Value = new Holder { RequestedMethod = requestedMethod };

    public void Clear() => Begin(null);
}
