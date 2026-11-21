using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// এক agent request-এর doc retrieval অবস্থা (handler → KB tool → handler → response)।
public interface IAgentRetrievalTrace
{
    // নতুন request শুরু: আগের hits মোছে এবং (ঐচ্ছিক) এই request-এর retrieval পদ্ধতি বসায়। handler-এর context-এ ডাকতে হয়।
    void Begin(string? requestedMethod);
    void Clear();

    // user যে পদ্ধতি বেছেছে (null/default = আগের পথ)।
    string? RequestedMethod { get; }

    void SetDocumentHits(IReadOnlyList<ScoredHit> hits);
    IReadOnlyList<ScoredHit> DocumentHits { get; }

    // আসলে যে পদ্ধতিতে chunk এসেছে (বাছা পদ্ধতি ব্যর্থ হলে তা থেকে আলাদা হতে পারে — যেমন rerank চাওয়া, কিন্তু "qdrant")।
    // কিছু পাওয়া না গেলে null। UI এটা দেখে জানায় বাছা পদ্ধতি সত্যিই চলেছে কিনা।
    void SetMethodUsed(string? method);
    string? MethodUsed { get; }

    // reranker চললে সেরা chunk-এর relevance score (০–১, "এই chunk প্রশ্নের উত্তর দেয় কিনা")। rerank পদ্ধতি না চললে null।
    // vector cosine-এর চেয়ে অনেক ভালো gate-সংকেত: ERP প্রশ্নে ≤০.০০৩, ডকুমেন্ট প্রশ্নে সাধারণত ≥০.১৫।
    void SetBestRerankScore(double? score);
    double? BestRerankScore { get; }

    // সেরা chunk-এর মূল vector cosine (hybrid/rerank-এও) — "প্রশ্নটা ডকুমেন্টের কিনা" gate-এর জন্য।
    void SetBestVectorScore(double? score);
    double? BestVectorScore { get; }
}
