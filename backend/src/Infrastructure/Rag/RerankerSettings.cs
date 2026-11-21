namespace AiPlatform.Infrastructure.Rag;

// appsettings "Reranker"। HuggingFace TEI (text-embeddings-inference) /rerank API — bge-reranker-v2-m3 (বহুভাষী, বাংলা সহ)।
// docker compose --profile rerank up -d reranker  (GPU; docs/troubleshooting.md §৬ঘ)
public class RerankerSettings
{
    public const string SectionName = "Reranker";

    // বন্ধ থাকলে rerank পদ্ধতিগুলো ব্যর্থ হয়ে আগের পথে (vector) ফেরে — নীরবে ভুল ক্রম নয়।
    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "http://localhost:8082";

    // vector (বা hybrid) থেকে এতগুলো candidate এনে reranker সাজায়, তারপর সেরা topN। বেশি = বেশি নির্ভুল, একটু ধীর।
    public int Candidates { get; init; } = 20;

    // reranker ধীর/বন্ধ হলে এর বেশি অপেক্ষা নয় — chat আটকে থাকবে না।
    public int TimeoutSeconds { get; init; } = 10;
}
