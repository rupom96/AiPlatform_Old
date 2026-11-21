namespace AiPlatform.Api.Contracts;

// Provider null/খালি হলে config-এর default (tool-capable হতে হবে)।
// Model = ollama model-picker override; null হলে provider-এর configured default model।
// RetrievalMethod (ঐচ্ছিক): default | qdrant-hybrid | qdrant-rerank | qdrant-hybrid-rerank — ডকুমেন্ট খোঁজার পদ্ধতি (তুলনার জন্য)।
public record AgentRequest(string Message, string? Provider, string? Model = null, string? RetrievalMethod = null);
