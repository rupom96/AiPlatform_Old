namespace AiPlatform.Api.Contracts;

// Method: keyword (default) | embedding | qdrant। Provider: LLM (null হলে default)।
// Model: ওই provider ওই model-এ (ollama model-picker; null = config default)।
public record RagRequest(string Question, string? Method, string? Provider, string? Model = null);
