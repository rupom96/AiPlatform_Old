namespace AiPlatform.Application.Common.Models;

// retrieval-এ পাওয়া এক টুকরো (citation)। Source = কোথা থেকে (doc নাম / "knowledge-base")।
// VectorScore = vector search-এর cosine similarity (থাকলে)। hybrid/rerank-এ Score বদলে যায় (RRF/relevance), কিন্তু
// "এই প্রশ্ন ডকুমেন্টের সাথে আদৌ মেলে কিনা" বোঝার gate-এর জন্য মূল cosine আলাদা রাখা হয়।
public record RetrievedChunk(string Text, double? Score, string? Source = null, double? VectorScore = null);

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
