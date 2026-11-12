namespace AiPlatform.Application.Common.Models;

// retrieval-এ পাওয়া এক টুকরো (citation)। Source = কোথা থেকে (doc নাম / "knowledge-base")।
public record RetrievedChunk(string Text, double? Score, string? Source = null);

// RAG উত্তর + কোন chunk থেকে (source/citation)। answer-এ [1],[2] = Sources-এর ক্রম।
// RetrievalMs = chunk খোঁজা, GenerationMs = LLM উত্তর লেখা। আলাদা রাখা হয়েছে কারণ ধীরগতির
// কারণ দুটো সম্পূর্ণ ভিন্ন — retrieval ধীর মানে embedding/Qdrant, generation ধীর মানে model।
public record RagResponse(
    string Answer,
    IReadOnlyList<RetrievedChunk> Sources,
    long RetrievalMs = 0,
    long GenerationMs = 0);

// vector store-এ এক point (UUID id + vector + text + কোন doc থেকে)।
public record VectorPoint(string Id, float[] Vector, string Text, string Source);
